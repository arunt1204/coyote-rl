using Microsoft.Coyote;
using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PaxosTests
{
    public class PaxosTest
    {
        public static void Execute(IActorRuntime runtime)
        {
            runtime.RegisterMonitor<SafetyMonitor>();
            runtime.CreateActor(typeof(ClusterManager), new ClusterManagerSetupEvent(runtime));
        }

        private class Proposal
        {
            public Proposal(string proposerName, int id)
            {
                this.ProposerName = proposerName;
                this.Id = id;
            }

            public string ProposerName { get; private set; }

            public int Id { get; private set; }

            public bool GreaterThan(Proposal p2)
            {
                if (this.Id != p2.Id)
                {
                    return this.Id > p2.Id;
                }

                return this.ProposerName.CompareTo(p2.ProposerName) > 0;
            }

            public override bool Equals(object obj)
            {
                if (obj == null)
                {
                    return false;
                }

                if (!(obj is Proposal))
                {
                    return false;
                }

                var other = (Proposal)obj;

                return this.Id == other.Id && this.ProposerName == other.ProposerName;
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }
        }

        private class AcceptorSetupEvent : Event
        {
            public AcceptorSetupEvent(string name, Dictionary<string, ActorId> proposers, Dictionary<string, ActorId> learners)
            {
                this.Name = name;
                this.Proposers = proposers;
                this.Learners = learners;
            }

            public string Name { get; private set; }

            public Dictionary<string, ActorId> Proposers { get; private set; }

            public Dictionary<string, ActorId> Learners { get; private set; }
        }

        private class AcceptRequest : Event
        {
            public AcceptRequest(ActorId from, Proposal proposal, string value)
            {
                this.From = from;
                this.Proposal = proposal;
                this.Value = value;
            }

            public ActorId From { get; private set; }

            public string Value { get; private set; }

            public Proposal Proposal { get; private set; }
        }

        private class ClientProposeValueRequest : Event
        {
            public ClientProposeValueRequest(ActorId from, string value)
            {
                this.From = from;
                this.Value = value;
            }

            public ActorId From { get; private set; }

            public string Value { get; private set; }
        }

        private class LearnerSetupEvent : Event
        {
            public LearnerSetupEvent(string name, Dictionary<string, ActorId> acceptors)
            {
                this.Name = name;
                this.Acceptors = acceptors;
            }

            public string Name { get; private set; }
            public Dictionary<string, ActorId> Acceptors { get; private set; }
        }

        private class ClusterManagerSetupEvent : Event
        {
            public ClusterManagerSetupEvent(IActorRuntime runtime)
            {
                this.Runtime = runtime;
            }

            public IActorRuntime Runtime { get; private set; }
        }

        private class ProposalRequest : Event
        {
            public ProposalRequest(ActorId from, Proposal proposal)
            {
                this.From = from;
                this.Proposal = proposal;
            }

            public ActorId From { get; private set; }

            public Proposal Proposal { get; private set; }
        }

        private class ProposerInitEvent : Event
        {
            public ProposerInitEvent(string name, Dictionary<string, ActorId> acceptors)
            {
                this.Name = name;
                this.Acceptors = acceptors;
            }

            public string Name { get; private set; }

            public Dictionary<string, ActorId> Acceptors { get; private set; }
        }

        private class ValueAcceptedEvent : Event
        {
            public ValueAcceptedEvent(ActorId acceptor, Proposal proposal, string value)
            {
                this.Proposal = proposal;
                this.Acceptor = acceptor;
                this.Value = value;
            }

            public ActorId Acceptor { get; private set; }

            public Proposal Proposal { get; private set; }

            public string Value { get; private set; }
        }

        private class ValueLearnedEvent : Event
        {
        }

        private class ProposalResponse : Event
        {
            public ProposalResponse(
                ActorId from,
                Proposal proposal,
                bool acknowledged,
                Proposal previouslyAcceptedProposal,
                string previouslyAcceptedValue)
            {
                this.From = from;
                this.Proposal = proposal;
                this.Acknowledged = acknowledged;
                this.PreviouslyAcceptedProposal = previouslyAcceptedProposal;
                this.PreviouslyAcceptedValue = previouslyAcceptedValue;
            }

            public ActorId From { get; private set; }

            public Proposal Proposal { get; private set; }

            public bool Acknowledged { get; private set; }

            public Proposal PreviouslyAcceptedProposal { get; private set; }

            public string PreviouslyAcceptedValue { get; private set; }
        }

        private class ClusterManager : StateMachine
        {
#pragma warning disable IDE0044 // Add readonly modifier
            private static int numProposers = 3;
            private static int numAcceptors = 5;
            private static int numLearners = 1;
            private static int maxAcceptorFailureCount = 2;
#pragma warning restore IDE0044 // Add readonly modifier

            private static Dictionary<string, ActorId> proposerNameToActorId;
            private static Dictionary<string, ActorId> acceptorNameToActorId;
            private static Dictionary<string, ActorId> learnerNameToActorId;

            public void InitOnEntry(Event e)
            {
                var initEvent = (ClusterManagerSetupEvent)e;
                var runtime = initEvent.Runtime;

                proposerNameToActorId = CreateActorIds(
                    runtime,
                    typeof(Proposer),
                    GetProposerName,
                    numProposers);

                acceptorNameToActorId = CreateActorIds(
                    runtime,
                    typeof(Acceptor),
                    GetAcceptorName,
                    numAcceptors);

                learnerNameToActorId = CreateActorIds(
                    runtime,
                    typeof(Learner),
                    GetLearnerName,
                    numLearners);

                foreach (var name in proposerNameToActorId.Keys)
                {
                    runtime.CreateActor(
                        proposerNameToActorId[name],
                        typeof(Proposer),
                        new ProposerInitEvent(
                            name,
                            acceptorNameToActorId));
                }

                foreach (var name in acceptorNameToActorId.Keys)
                {
                    runtime.CreateActor(
                        acceptorNameToActorId[name],
                        typeof(Acceptor),
                        new AcceptorSetupEvent(
                            name,
                            proposerNameToActorId,
                            learnerNameToActorId));
                }

                foreach (var name in learnerNameToActorId.Keys)
                {
                    runtime.CreateActor(
                        learnerNameToActorId[name],
                        typeof(Learner),
                        new LearnerSetupEvent(
                            name,
                            acceptorNameToActorId));
                }

                for (int i = 0; i < numProposers; i++)
                {
                    var proposerName = GetProposerName(i);
                    var value = GetValue(i);
                    runtime.SendEvent(proposerNameToActorId[proposerName], new ClientProposeValueRequest(null, value));
                }

                int failureCount = 0;
                for (int i = 0; i < numAcceptors; i++)
                {
                    if (this.RandomBoolean() && failureCount < maxAcceptorFailureCount)
                    {
                        failureCount++;
                        this.SendEvent(acceptorNameToActorId[GetAcceptorName(i)], HaltEvent.Instance);
                    }
                }
            }

#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            private static Dictionary<string, ActorId> CreateActorIds(
              IActorRuntime runtime,
              Type StateMachineType,
              Func<int, string> StateMachineNameFunc,
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
              int numStateMachines)
            {
                var result = new Dictionary<string, ActorId>();

                for (int i = 0; i < numStateMachines; i++)
                {
                    var name = StateMachineNameFunc(i);
#pragma warning disable SA1312 // Variable names should begin with lower-case letter
                    var ActorId = runtime.CreateActorIdFromName(StateMachineType, name);
#pragma warning restore SA1312 // Variable names should begin with lower-case letter
                    result[name] = ActorId;
                }

                return result;
            }

            private static string GetProposerName(int index)
            {
                return "p" + (index + 1);
            }

            private static string GetAcceptorName(int index)
            {
                return "a" + (index + 1);
            }

            private static string GetLearnerName(int index)
            {
                return "l" + (index + 1);
            }

            private static string GetValue(int index)
            {
                return "v" + (index + 1);
            }

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            internal class Init : State
            {
            }
        }

        private class Proposer : StateMachine
        {
            private string name;
            private Dictionary<string, ActorId> acceptors;

            private int proposalCounter = 0;

            private string value;
            private Proposal currentProposal = null;
            private HashSet<ActorId> receivedProposalResponses = null;

            private readonly Dictionary<ActorId, Proposal> acceptorToPreviouslyAcceptedProposal = new Dictionary<ActorId, Proposal>();
            private readonly Dictionary<ActorId, string> acceptorToPreviouslyAcceptedValue = new Dictionary<ActorId, string>();

            private bool finalValueChosen = false;

            protected override int HashedState
            {
                get
                {
                    int hash = 37;
                    hash = (hash * 397) + this.finalValueChosen.GetHashCode();
                    hash = (hash * 397) + this.proposalCounter.GetHashCode();
                    if (this.value != null)
                    {
                        hash = (hash * 397) + this.value.GetHashCode();
                    }

                    return hash;
                }
            }

            public void InitOnEntry(Event e)
            {
                var initEvent = (ProposerInitEvent)e;

                this.name = initEvent.Name;
                this.acceptors = initEvent.Acceptors;

                this.RaiseGotoStateEvent<Ready>();
            }

            public void ProposeValueRequestHandler(Event e)
            {
                var request = (ClientProposeValueRequest)e;

                this.value = request.Value;
                this.proposalCounter++;
                this.receivedProposalResponses = new HashSet<ActorId>();
                this.currentProposal = new Proposal(this.name, this.proposalCounter);

                foreach (var acceptor in this.acceptors.Values)
                {
                    this.SendEvent(acceptor, new ProposalRequest(this.Id, this.currentProposal));
                }

                this.RaiseGotoStateEvent<WaitingForAcks>();
            }

            public void ProposalResponseHandler(Event e)
            {
                var response = (ProposalResponse)e;

                if (!response.Acknowledged)
                {
                    return;
                }

                var fromAcceptor = response.From;
                var previouslyAcceptedProposal = response.PreviouslyAcceptedProposal;
                var previouslyAcceptedValue = response.PreviouslyAcceptedValue;

                if (previouslyAcceptedProposal != null)
                {
                    this.acceptorToPreviouslyAcceptedProposal[fromAcceptor] = previouslyAcceptedProposal;
                    this.acceptorToPreviouslyAcceptedValue[fromAcceptor] = previouslyAcceptedValue;
                }

                this.receivedProposalResponses.Add(response.From);

                bool greaterThan50PercentObservations = (this.receivedProposalResponses.Count / (double)this.acceptors.Count) > 0.5;
                if (!this.finalValueChosen && greaterThan50PercentObservations)
                {
                    Proposal chosenPreviouslyAcceptedProposal = null;
                    string chosenValue = null;
                    foreach (var acceptor in this.acceptorToPreviouslyAcceptedProposal.Keys)
                    {
                        var proposal = this.acceptorToPreviouslyAcceptedProposal[acceptor];
                        if (chosenPreviouslyAcceptedProposal == null)
                        {
                            chosenPreviouslyAcceptedProposal = proposal;
                            chosenValue = this.acceptorToPreviouslyAcceptedValue[acceptor];
                        }
                        else if (!proposal.GreaterThan(chosenPreviouslyAcceptedProposal))
                        {
                            chosenPreviouslyAcceptedProposal = proposal;
                            chosenValue = this.acceptorToPreviouslyAcceptedValue[acceptor];
                        }
                    }

                    if (chosenPreviouslyAcceptedProposal != null)
                    {
                        if(chosenValue == null)
                        {
                            Console.WriteLine("####################ASSERT FAILED-1######################");
                            this.Logger.WriteLine("####################ASSERT FAILED-1######################");
                        }

                        this.Assert(chosenValue != null);
                        this.value = chosenValue;
                    }

                    this.finalValueChosen = true;

                    foreach (var acceptor in this.receivedProposalResponses)
                    {
                        this.SendEvent(acceptor, new AcceptRequest(this.Id, this.currentProposal, this.value));
                    }
                }
                else if (this.finalValueChosen)
                {
                    this.SendEvent(fromAcceptor, new AcceptRequest(this.Id, this.currentProposal, this.value));
                }
            }

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            internal class Init : State
            {
            }

            [OnEventDoAction(typeof(ClientProposeValueRequest), nameof(ProposeValueRequestHandler))]
            internal class Ready : State
            {
            }

            [OnEventDoAction(typeof(ProposalResponse), nameof(ProposalResponseHandler))]
            internal class WaitingForAcks : State
            {
            }
        }

        private class Acceptor : StateMachine
        {
            private string name;
            private Dictionary<string, ActorId> proposers;
            private Dictionary<string, ActorId> learners;

            private Proposal lastAckedProposal;

            private Proposal lastAcceptedProposal;
            private string acceptedValue;

            protected override int HashedState
            {
                get
                {
                    int hash = 37;
                    if (this.lastAckedProposal != null)
                    {
                        hash = (hash * 397) + this.lastAckedProposal.ProposerName.GetHashCode();
                        hash = (hash * 397) + this.lastAckedProposal.Id.GetHashCode();
                    }

                    if (this.lastAcceptedProposal != null)
                    {
                        hash = (hash * 397) + this.lastAcceptedProposal.ProposerName.GetHashCode();
                        hash = (hash * 397) + this.lastAcceptedProposal.Id.GetHashCode();
                    }

                    if (this.acceptedValue != null)
                    {
                        hash = (hash * 397) + this.acceptedValue.GetHashCode();
                    }

                    return hash;
                }
            }

            public void InitOnEntry(Event e)
            {
                var initEvent = (AcceptorSetupEvent)e;

                this.name = initEvent.Name;
                this.proposers = initEvent.Proposers;
                this.learners = initEvent.Learners;
            }

            public void ProposalRequestHandler(Event e)
            {
                var proposalRequest = (ProposalRequest)e;

                var proposer = proposalRequest.From;
                var proposal = proposalRequest.Proposal;

                if (this.lastAckedProposal == null ||
                     proposal.GreaterThan(this.lastAckedProposal))
                {
                    this.lastAckedProposal = proposal;
                    this.SendEvent(proposer, new ProposalResponse(
                        this.Id,
                        proposal,
                        true /* acknowledged */,
                        this.lastAcceptedProposal,
                        this.acceptedValue));
                }
            }

            public void AcceptRequestHandler(Event e)
            {
                var acceptRequest = (AcceptRequest)e;
                var proposal = acceptRequest.Proposal;
                var value = acceptRequest.Value;

                if (this.lastAckedProposal == null ||
                    !this.lastAckedProposal.Equals(proposal))
                {
                    return;
                }

                this.lastAcceptedProposal = proposal;
                this.acceptedValue = value;

                foreach (var learner in this.learners.Values)
                {
                    this.SendEvent(learner, new ValueAcceptedEvent(this.Id, proposal, value));
                }
            }

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            [OnEventDoAction(typeof(ProposalRequest), nameof(ProposalRequestHandler))]
            [OnEventDoAction(typeof(AcceptRequest), nameof(AcceptRequestHandler))]
            internal class Init : State
            {
            }
        }

        private class Learner : StateMachine
        {
            private string name;
            private Dictionary<string, ActorId> acceptors;

            private readonly Dictionary<ActorId, Proposal> acceptorToProposalMap = new Dictionary<ActorId, Proposal>();
            private readonly Dictionary<Proposal, string> proposalToValueMap = new Dictionary<Proposal, string>();

            private string learnedValue = null;

            protected override int HashedState
            {
                get
                {
                    int hash = 37;
                    if (this.learnedValue != null)
                    {
                        hash = (hash * 397) + this.learnedValue.GetHashCode();
                    }

                    return hash;
                }
            }

            public void InitOnEntry(Event e)
            {
                var initEvent = (LearnerSetupEvent)e;

                this.name = initEvent.Name;
                this.acceptors = initEvent.Acceptors;
            }

            public void ValueAcceptedEventHandler(Event e)
            {
                var acceptedEvent = (ValueAcceptedEvent)e;

                var acceptor = acceptedEvent.Acceptor;
                var acceptedProposal = acceptedEvent.Proposal;
                var value = acceptedEvent.Value;

                this.acceptorToProposalMap[acceptor] = acceptedProposal;
                this.proposalToValueMap[acceptedProposal] = value;

                var proposalGroups = this.acceptorToProposalMap.Values.GroupBy(p => p);
                var majorityProposalList = proposalGroups.OrderByDescending(g => g.Count()).First();

                bool greaterThan50PercentObservations = (majorityProposalList.Count() / (double)this.acceptors.Count) > 0.5;
                if (greaterThan50PercentObservations)
                {
                    var majorityProposal = majorityProposalList.First();
                    var majorityValue = this.proposalToValueMap[majorityProposal];

                    if (this.learnedValue == null)
                    {
                        this.learnedValue = majorityValue;
                        this.Monitor<SafetyMonitor>(new ValueLearnedEvent());
                    }
                    else
                    {
                        if (this.learnedValue == majorityValue)
                        {
                            this.Monitor<SafetyMonitor>(new ValueLearnedEvent());
                        }
                        else
                        {
                            Console.WriteLine("####################ASSERT FAILED-2######################");
                            this.Logger.WriteLine("####################ASSERT FAILED-2######################");
                            this.Assert(false, "Conflicting values learned");
                        }
                    }
                }
            }

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            [OnEventDoAction(typeof(ValueAcceptedEvent), nameof(ValueAcceptedEventHandler))]
            internal class Init : State
            {
            }
        }

        private class SafetyMonitor : Monitor
        {
            [Start]
            [OnEventGotoState(typeof(ValueLearnedEvent), typeof(ValueLearned))]
            internal class Init : State
            {
            }

            [OnEventGotoState(typeof(ValueLearnedEvent), typeof(ValueLearned))]
            internal class ValueLearned : State
            {
            }
        }
    }

    public class Paxos
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }

        [Microsoft.Coyote.SystematicTesting.TestAttribute]
        public static async Task TestPaxos()
        {
            // CoyoteRuntime.Current.
            // TestFib tf = new TestFib(1, 1, 11);
            // await tf.TestRun();

            var configuration = Configuration.Create().WithVerbosityEnabled();

            // Creates a new P# runtime instance, and passes an optional configuration.
            var runtime = Microsoft.Coyote.Actors.RuntimeFactory.Create(configuration);

            PaxosTest.Execute(runtime);
        }
    }
}
