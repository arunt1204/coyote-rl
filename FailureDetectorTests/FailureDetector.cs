using Microsoft.Coyote;
using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FailureDetectorTests
{
    public class FailureDetectorTest
    {
        public static void Execute(IActorRuntime runtime)
        {
            runtime.RegisterMonitor<Safety>();
            runtime.CreateActor(typeof(ClusterManager), new ClusterManager.Config(2));
        }

        private class ClusterManager : StateMachine
        {
            internal class Config : Event
            {
                public int NumOfNodes;

                public Config(int numOfNodes)
                {
                    this.NumOfNodes = numOfNodes;
                }
            }

            internal class RegisterClient : Event
            {
                public ActorId Client;

                public RegisterClient(ActorId client)
                {
                    this.Client = client;
                }
            }

            internal class UnregisterClient : Event
            {
                public ActorId Client;

                public UnregisterClient(ActorId client)
                {
                    this.Client = client;
                }
            }

            private ActorId FDMachine;
            private HashSet<ActorId> Nodes;
            private int NumOfNodes;

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            public void InitOnEntry(Event e)
            {
                this.NumOfNodes = (e as Config).NumOfNodes;

                // Initializes the nodes.
                this.Nodes = new HashSet<ActorId>();
                for (int i = 0; i < this.NumOfNodes; i++)
                {
                    var node = this.CreateActor(typeof(Node));
                    this.Nodes.Add(node);
                }

                this.FDMachine = this.CreateActor(typeof(FDMachine), new FDMachine.Config(this.Nodes));
                this.SendEvent(this.FDMachine, new RegisterClient(this.Id));

                this.RaiseGotoStateEvent<InjectFailures>();
            }

            [OnEntry(nameof(InjectFailuresOnEntry))]
            public class InjectFailures : State
            {
            }

            /// <summary>
            /// Injects failures (modelled with the special P# event 'halt').
            /// </summary>
            private void InjectFailuresOnEntry()
            {
                foreach (var node in this.Nodes)
                {
                    this.SendEvent(node, HaltEvent.Instance);
                }
            }
        }

        /// <summary>
        /// Implementation of a failure detector P# machine.
        /// </summary>
        private class FDMachine : StateMachine
        {
            internal class Config : Event
            {
                public HashSet<ActorId> Nodes;

                public Config(HashSet<ActorId> nodes)
                {
                    this.Nodes = nodes;
                }
            }

            private class TimerCancelled : Event
            {
            }

            private class RoundDone : Event
            {
            }

            private class Unit : Event
            {
            }

            /// <summary>
            /// Nodes to be monitored.
            /// </summary>
            private HashSet<ActorId> Nodes;

            /// <summary>
            /// Set of registered clients.
            /// </summary>
            private HashSet<ActorId> Clients;

            /// <summary>
            /// Number of made 'Ping' attempts.
            /// </summary>
            private int Attempts;

            /// <summary>
            /// Set of alive nodes.
            /// </summary>
            private HashSet<ActorId> Alive;

            /// <summary>
            /// Collected responses in one round.
            /// </summary>
            private HashSet<ActorId> Responses;

            /// <summary>
            /// Reference to the timer machine.
            /// </summary>
            private ActorId Timer;

            protected override int HashedState
            {
                get
                {
                    unchecked
                    {
                        int hash = 14689;
                        if (this.Alive != null)
                        {
                            int setHash = 19;
                            foreach (var alive in this.Alive)
                            {
                                int aliveHash = 37;
                                aliveHash += (aliveHash * 397) + alive.GetHashCode();
                                setHash *= aliveHash;
                            }

                            hash += setHash;
                        }

                        if (this.Responses != null)
                        {
                            int setHash = 19;
                            foreach (var response in this.Responses)
                            {
                                int responseHash = 37;
                                responseHash += (responseHash * 397) + responseHash.GetHashCode();
                                setHash *= responseHash;
                            }

                            hash += setHash;
                        }

                        hash += this.Attempts.GetHashCode();
                        return hash;
                    }
                }
            }

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            [OnEventDoAction(typeof(ClusterManager.RegisterClient), nameof(RegisterClientAction))]
            [OnEventDoAction(typeof(ClusterManager.UnregisterClient), nameof(UnregisterClientAction))]
            [OnEventPushState(typeof(Unit), typeof(SendPing))]
            private class Init : State
            {
            }

            private void InitOnEntry(Event e)
            {
                var nodes = (e as Config).Nodes;

                this.Nodes = new HashSet<ActorId>(nodes);
                this.Clients = new HashSet<ActorId>();
                this.Alive = new HashSet<ActorId>();
                this.Responses = new HashSet<ActorId>();

                // Initializes the alive set to contain all available nodes.
                foreach (var node in this.Nodes)
                {
                    this.Alive.Add(node);
                }

                // Initializes the timer.
                this.Timer = this.CreateActor(typeof(Timer), new Timer.Config(this.Id));

                // Transitions to the 'SendPing' state after everything has initialized.
                this.RaiseEvent(new Unit());
            }

            private void RegisterClientAction(Event e)
            {
                var client = (e as ClusterManager.RegisterClient).Client;
                this.Clients.Add(client);
            }

            private void UnregisterClientAction(Event e)
            {
                var client = (e as ClusterManager.UnregisterClient).Client;
                if (this.Clients.Contains(client))
                {
                    this.Clients.Remove(client);
                }
            }

            [OnEntry(nameof(SendPingOnEntry))]
            [OnEventGotoState(typeof(RoundDone), typeof(Reset))]
            [OnEventPushState(typeof(TimerCancelled), typeof(WaitForCancelResponse))]
            [OnEventDoAction(typeof(Node.Pong), nameof(PongAction))]
            [OnEventDoAction(typeof(Timer.TimeoutEvent), nameof(TimeoutAction))]
            private class SendPing : State
            {
            }

            private void SendPingOnEntry()
            {
                foreach (var node in this.Nodes)
                {
                    // Sends a 'Ping' event to any machine that has not responded.
                    if (this.Alive.Contains(node) && !this.Responses.Contains(node))
                    {
                        this.Monitor<Safety>(new Safety.Ping(node));
                        this.SendEvent(node, new Node.Ping(this.Id));
                    }
                }

                // Starts the timer with a given timeout value. Note that in this sample,
                // the timeout value is not actually used, because the timer is abstracted
                // away using P# to enable systematic testing (i.e. timeouts are triggered
                // nondeterministically). In production, this model timer machine will be
                // replaced by a real timer.
                this.SendEvent(this.Timer, new Timer.StartTimerEvent(100));
            }

            /// <summary>
            /// This action is triggered whenever a node replies with a 'Pong' event.
            /// </summary>
            private void PongAction(Event e)
            {
                var node = (e as Node.Pong).Node;
                if (this.Alive.Contains(node))
                {
                    this.Responses.Add(node);

                    // Checks if the status of alive nodes has changed.
                    if (this.Responses.Count == this.Alive.Count)
                    {
                        this.SendEvent(this.Timer, new Timer.CancelTimerEvent());
                        this.RaiseEvent(new TimerCancelled());
                    }
                }
            }

            private void TimeoutAction()
            {
                // One attempt is done for this round.
                this.Attempts++;

                // Each round has a maximum number of 2 attempts.
                if (this.Responses.Count < this.Alive.Count && this.Attempts < 2)
                {
                    // Retry by looping back to same state.
                    this.RaiseGotoStateEvent<SendPing>();
                }
                else
                {
                    foreach (var node in this.Nodes)
                    {
                        if (this.Alive.Contains(node) && !this.Responses.Contains(node))
                        {
                            this.Alive.Remove(node);
                        }
                    }

                    this.RaiseEvent(new RoundDone());
                }
            }

            [OnEventDoAction(typeof(Timer.CancelSuccess), nameof(CancelSuccessAction))]
            [OnEventDoAction(typeof(Timer.CancelFailure), nameof(CancelFailure))]
            [DeferEvents(typeof(Timer.TimeoutEvent), typeof(Node.Pong))]
            private class WaitForCancelResponse : State
            {
            }

            private void CancelSuccessAction()
            {
                this.RaiseEvent(new RoundDone());
            }

            private void CancelFailure()
            {
                this.RaisePopStateEvent();
            }

            [OnEntry(nameof(ResetOnEntry))]
            [OnEventGotoState(typeof(Timer.TimeoutEvent), typeof(SendPing))]
            [IgnoreEvents(typeof(Node.Pong))]
            private class Reset : State
            {
            }

            /// <summary>
            /// Prepares the failure detector for the next round.
            /// </summary>
            private void ResetOnEntry()
            {
                this.Attempts = 0;
                this.Responses.Clear();

                // Starts the timer with a given timeout value (see details above).
                this.SendEvent(this.Timer, new Timer.StartTimerEvent(1000));
            }
        }

        private class Node : StateMachine
        {
            internal class Ping : Event
            {
                public ActorId Client;

                public Ping(ActorId client)
                {
                    this.Client = client;
                }
            }

            internal class Pong : Event
            {
                public ActorId Node;

                public Pong(ActorId node)
                {
                    this.Node = node;
                }
            }

            [Start]
            [OnEventDoAction(typeof(Ping), nameof(SendPong))]
            private class WaitPing : State
            {
            }

            private void SendPong(Event e)
            {
                var client = (e as Ping).Client;
                this.Monitor<Safety>(new Safety.Pong(this.Id));
                this.SendEvent(client, new Pong(this.Id));
            }
        }

        private class Timer : StateMachine
        {
            internal class Config : Event
            {
                public ActorId Target;

                public Config(ActorId target)
                {
                    this.Target = target;
                }
            }

            /// <summary>
            /// Although this event accepts a timeout value, because
            /// this machine models a timer by nondeterministically
            /// triggering a timeout, this value is not used.
            /// </summary>
            internal class StartTimerEvent : Event
            {
                public int Timeout;

                public StartTimerEvent(int timeout)
                {
                    this.Timeout = timeout;
                }
            }

            internal class TimeoutEvent : Event
            {
            }

            internal class CancelSuccess : Event
            {
            }

            internal class CancelFailure : Event
            {
            }

            internal class CancelTimerEvent : Event
            {
            }

            /// <summary>
            /// Reference to the owner of the timer.
            /// </summary>
            private ActorId Target;

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            /// <summary>
            /// When it enters the 'Init' state, the timer receives a reference to
            /// the target machine, and then transitions to the 'WaitForReq' state.
            /// </summary>
            private void InitOnEntry(Event e)
            {
                this.Target = (e as Config).Target;
                this.RaiseGotoStateEvent<WaitForReq>();
            }

            [OnEventGotoState(typeof(CancelTimerEvent), typeof(WaitForReq), nameof(CancelTimerAction))]
            [OnEventGotoState(typeof(StartTimerEvent), typeof(WaitForCancel))]
            private class WaitForReq : State
            {
            }

            private void CancelTimerAction()
            {
                this.SendEvent(this.Target, new CancelFailure());
            }

            [IgnoreEvents(typeof(StartTimerEvent))]
            [OnEventGotoState(typeof(CancelTimerEvent), typeof(WaitForReq), nameof(CancelTimerAction2))]
            // [OnEventGotoState(typeof(Default), typeof(WaitForReq), nameof(DefaultAction))]
            [OnEventGotoState(typeof(Init), typeof(WaitForReq), nameof(DefaultAction))]
            private class WaitForCancel : State
            {
            }

            private void DefaultAction()
            {
                this.SendEvent(this.Target, new TimeoutEvent());
            }

            /// <summary>
            /// The response to a 'CancelTimer' event is nondeterministic. During testing, P# will
            /// take control of this source of nondeterminism and explore different execution paths.
            ///
            /// Using this approach, we model the race condition between the arrival of a 'CancelTimer'
            /// event from the target and the elapse of the timer.
            /// </summary>
            private void CancelTimerAction2()
            {
                // A nondeterministic choice that is controlled by the P# runtime during testing.
                if (this.RandomBoolean())
                {
                    this.SendEvent(this.Target, new CancelSuccess());
                }
                else
                {
                    this.SendEvent(this.Target, new CancelFailure());
                    this.SendEvent(this.Target, new TimeoutEvent());
                }
            }
        }

        private class Safety : Monitor
        {
            internal class Ping : Event
            {
                public ActorId Client;

                public Ping(ActorId client)
                {
                    this.Client = client;
                }
            }

            internal class Pong : Event
            {
                public ActorId Node;

                public Pong(ActorId node)
                {
                    this.Node = node;
                }
            }

            private Dictionary<ActorId, int> Pending;

            protected override int HashedState
            {
                get
                {
                    int hash = 14689;

                    if (this.Pending != null)
                    {
                        foreach (var pending in this.Pending)
                        {
                            int pendingHash = 37;
                            pendingHash += (pendingHash * 397) + pending.Key.GetHashCode();
                            pendingHash += (pendingHash * 397) + pending.Value.GetHashCode();
                            hash *= pendingHash;
                        }
                    }

                    return hash;
                }
            }

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            [OnEventDoAction(typeof(Ping), nameof(PingAction))]
            [OnEventDoAction(typeof(Pong), nameof(PongAction))]
            private class Init : State
            {
            }

            private void InitOnEntry()
            {
                this.Pending = new Dictionary<ActorId, int>();
            }

            private void PingAction(Event e)
            {
                var client = (e as Ping).Client;
                if (!this.Pending.ContainsKey(client))
                {
                    this.Pending[client] = 0;
                }

                this.Pending[client] = this.Pending[client] + 1;
                this.Assert(this.Pending[client] <= 3, $"'{client}' ping count must be <= 3.");
                // this.Assert(this.Pending[client] <= 3, $"Violation in safety monitor.");
            }

            private void PongAction(Event e)
            {
                var node = (e as Pong).Node;
                this.Assert(this.Pending.ContainsKey(node), $"'{node}' is not in pending set.");
                this.Assert(this.Pending[node] > 0, $"'{node}' ping count must be > 0.");
                // this.Assert(this.Pending.ContainsKey(node), $"Violation in safety monitor.");
                // this.Assert(this.Pending[node] > 0, $"Violation in safety monitor.");
                this.Pending[node] = this.Pending[node] - 1;
            }
        }
    }

    public class FailureDetector
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }

        [Microsoft.Coyote.SystematicTesting.TestAttribute]
        public static async Task TestFD()
        {
            // CoyoteRuntime.Current.
            // TestFib tf = new TestFib(1, 1, 11);
            // await tf.TestRun();

            var configuration = Configuration.Create().WithVerbosityEnabled();

            // Creates a new P# runtime instance, and passes an optional configuration.
            var runtime = Microsoft.Coyote.Actors.RuntimeFactory.Create(configuration);

            FailureDetectorTest.Execute(runtime);
        }
    }
}
