﻿using Microsoft.Coyote;
using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChordTests
{
    public class ChordTest
    {
        public static void Execute(IActorRuntime runtime)
        {
            runtime.RegisterMonitor<LivenessMonitor>();
            runtime.CreateActor(typeof(ClusterManager));
        }

        private class Finger
        {
            public int Start;
            public int End;
            public ActorId Node;

            public Finger(int start, int end, ActorId node)
            {
                this.Start = start;
                this.End = end;
                this.Node = node;
            }
        }

        private class ClusterManager : StateMachine
        {
            private class CreateNewNode : Event { }
            private class TerminateNode : Event { }
            private class Local : Event { }

            int NumOfNodes;
            int NumOfIds;

            List<ActorId> ChordNodes;

            List<int> Keys;
            List<int> NodeIds;

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            [OnEventGotoState(typeof(Local), typeof(Waiting))]
            class Init : State { }

            void InitOnEntry()
            {
                this.NumOfNodes = 5;
                this.NumOfIds = (int)Math.Pow(2, this.NumOfNodes);

                this.ChordNodes = new List<ActorId>();
                this.NodeIds = new List<int> { 0, 1, 3, 5, 7 };
                this.Keys = new List<int> {
                    1, 2, 4, 6, 9, 11,
                    13, 22, 27, 29, 33,
                    38, 45, 53, 67, 72,
                    81, 95, 101, 102, 104,
                    106, 109, 111, 113, 122,
                    127, 129, 133, 138, 145,
                    153, 167, 172, 181, 195
                };

                for (int idx = 0; idx < this.NodeIds.Count; idx++)
                {
                    this.ChordNodes.Add(this.CreateActor(typeof(ChordNode)));
                }

                var nodeKeys = this.AssignKeysToNodes();
                for (int idx = 0; idx < this.ChordNodes.Count; idx++)
                {
                    var nodeId = this.NodeIds[idx];
                    if (nodeKeys.ContainsKey(nodeId))
                    {
                        var keys = nodeKeys[nodeId];
                        this.SendEvent(this.ChordNodes[idx], new ChordNode.Config(nodeId, new HashSet<int>(keys),
                            new List<ActorId>(this.ChordNodes), new List<int>(this.NodeIds), this.Id));
                    }
                    else
                    {
                        this.SendEvent(this.ChordNodes[idx], new ChordNode.Config(nodeId, new HashSet<int>(),
                            new List<ActorId>(this.ChordNodes), new List<int>(this.NodeIds), this.Id));
                    }
                }

                this.CreateActor(typeof(Client),
                    new Client.Config(this.Id, new List<int>(this.Keys)));

                this.RaiseEvent(new Local());
            }

            [OnEventDoAction(typeof(ChordNode.FindSuccessor), nameof(ForwardFindSuccessor))]
            [OnEventDoAction(typeof(CreateNewNode), nameof(ProcessCreateNewNode))]
            [OnEventDoAction(typeof(TerminateNode), nameof(ProcessTerminateNode))]
            [OnEventDoAction(typeof(ChordNode.JoinAck), nameof(QueryStabilize))]
            class Waiting : State { }

            void ForwardFindSuccessor(Event e)
            {
                this.SendEvent(this.ChordNodes[0], e);
            }

            void ProcessCreateNewNode()
            {
                int newId = -1;
                while ((newId < 0 || this.NodeIds.Contains(newId)) &&
                    this.NodeIds.Count < this.NumOfIds)
                {
                    for (int i = 0; i < this.NumOfIds; i++)
                    {
                        if (this.RandomBoolean())
                        {
                            newId = i;
                        }
                    }
                }

                this.Assert(newId >= 0, "Cannot create a new node, no ids available.");

                var newNode = this.CreateActor(typeof(ChordNode));

                this.NumOfNodes++;
                this.NodeIds.Add(newId);
                this.ChordNodes.Add(newNode);

                this.SendEvent(newNode, new ChordNode.Join(newId, new List<ActorId>(this.ChordNodes),
                    new List<int>(this.NodeIds), this.NumOfIds, this.Id));
            }

            void ProcessTerminateNode()
            {
                int endId = -1;
                while ((endId < 0 || !this.NodeIds.Contains(endId)) &&
                    this.NodeIds.Count > 0)
                {
                    for (int i = 0; i < this.ChordNodes.Count; i++)
                    {
                        if (this.RandomBoolean())
                        {
                            endId = i;
                        }
                    }
                }

                this.Assert(endId >= 0, "Cannot find a node to terminate.");

                var endNode = this.ChordNodes[endId];

                this.NumOfNodes--;
                this.NodeIds.Remove(endId);
                this.ChordNodes.Remove(endNode);

                this.SendEvent(endNode, new ChordNode.Terminate());
            }

            void QueryStabilize()
            {
                foreach (var node in this.ChordNodes)
                {
                    this.SendEvent(node, new ChordNode.Stabilize());
                }
            }

            Dictionary<int, List<int>> AssignKeysToNodes()
            {
                var nodeKeys = new Dictionary<int, List<int>>();
                for (int i = this.Keys.Count - 1; i >= 0; i--)
                {
                    bool assigned = false;
                    for (int j = 0; j < this.NodeIds.Count; j++)
                    {
                        if (this.Keys[i] <= this.NodeIds[j])
                        {
                            if (nodeKeys.ContainsKey(this.NodeIds[j]))
                            {
                                nodeKeys[this.NodeIds[j]].Add(this.Keys[i]);
                            }
                            else
                            {
                                nodeKeys.Add(this.NodeIds[j], new List<int>());
                                nodeKeys[this.NodeIds[j]].Add(this.Keys[i]);
                            }

                            assigned = true;
                            break;
                        }
                    }

                    if (!assigned)
                    {
                        if (nodeKeys.ContainsKey(this.NodeIds[0]))
                        {
                            nodeKeys[this.NodeIds[0]].Add(this.Keys[i]);
                        }
                        else
                        {
                            nodeKeys.Add(this.NodeIds[0], new List<int>());
                            nodeKeys[this.NodeIds[0]].Add(this.Keys[i]);
                        }
                    }
                }

                return nodeKeys;
            }
        }

        private class ChordNode : StateMachine
        {
            internal class Config : Event
            {
                public int Id;
                public HashSet<int> Keys;
                public List<ActorId> Nodes;
                public List<int> NodeIds;
                public ActorId Manager;

                public Config(int id, HashSet<int> keys, List<ActorId> nodes,
                    List<int> nodeIds, ActorId manager)
                    : base()
                {
                    this.Id = id;
                    this.Keys = keys;
                    this.Nodes = nodes;
                    this.NodeIds = nodeIds;
                    this.Manager = manager;
                }
            }

            internal class Join : Event
            {
                public int Id;
                public List<ActorId> Nodes;
                public List<int> NodeIds;
                public int NumOfIds;
                public ActorId Manager;

                public Join(int id, List<ActorId> nodes, List<int> nodeIds,
                    int numOfIds, ActorId manager)
                    : base()
                {
                    this.Id = id;
                    this.Nodes = nodes;
                    this.NodeIds = nodeIds;
                    this.NumOfIds = numOfIds;
                    this.Manager = manager;
                }
            }

            internal class FindSuccessor : Event
            {
                public ActorId Sender;
                public int Key;

                public FindSuccessor(ActorId sender, int key)
                    : base()
                {
                    this.Sender = sender;
                    this.Key = key;
                }
            }

            internal class FindSuccessorResp : Event
            {
                public ActorId Node;
                public int Key;

                public FindSuccessorResp(ActorId node, int key)
                    : base()
                {
                    this.Node = node;
                    this.Key = key;
                }
            }

            internal class FindPredecessor : Event
            {
                public ActorId Sender;

                public FindPredecessor(ActorId sender)
                    : base()
                {
                    this.Sender = sender;
                }
            }

            internal class FindPredecessorResp : Event
            {
                public ActorId Node;

                public FindPredecessorResp(ActorId node)
                    : base()
                {
                    this.Node = node;
                }
            }

            internal class QueryId : Event
            {
                public ActorId Sender;

                public QueryId(ActorId sender)
                    : base()
                {
                    this.Sender = sender;
                }
            }

            internal class QueryIdResp : Event
            {
                public int Id;

                public QueryIdResp(int id)
                    : base()
                {
                    this.Id = id;
                }
            }

            internal class AskForKeys : Event
            {
                public ActorId Node;
                public int Id;

                public AskForKeys(ActorId node, int id)
                    : base()
                {
                    this.Node = node;
                    this.Id = id;
                }
            }

            internal class AskForKeysResp : Event
            {
                public List<int> Keys;

                public AskForKeysResp(List<int> keys)
                    : base()
                {
                    this.Keys = keys;
                }
            }

            internal class NotifySuccessor : Event
            {
                public ActorId Node;

                public NotifySuccessor(ActorId node)
                    : base()
                {
                    this.Node = node;
                }
            }

            internal class JoinAck : Event { }
            internal class Stabilize : Event { }
            internal class Terminate : Event { }
            internal class Local : Event { }

            int NodeId;
            HashSet<int> Keys;
            int NumOfIds;

            Dictionary<int, Finger> FingerTable;
            ActorId Predecessor;

            ActorId Manager;

            protected override int HashedState
            {
                get
                {
                    int hash = 14689;

                    if (this.Keys != null)
                    {
                        foreach (var key in this.Keys)
                        {
                            int keyHash = 37;
                            keyHash += (keyHash * 397) + key.GetHashCode();
                            hash *= keyHash;
                        }
                    }

                    return hash;
                }
            }

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            [OnEventGotoState(typeof(Local), typeof(Waiting))]
            [OnEventDoAction(typeof(Config), nameof(Configure))]
            [OnEventDoAction(typeof(Join), nameof(JoinCluster))]
            [DeferEvents(typeof(AskForKeys), typeof(FindPredecessor), typeof(FindSuccessor),
                typeof(NotifySuccessor), typeof(Stabilize), typeof(Terminate))]
            class Init : State { }

            void InitOnEntry()
            {
                this.FingerTable = new Dictionary<int, Finger>();
            }

            void Configure(Event e)
            {
                this.NodeId = (e as Config).Id;
                this.Keys = (e as Config).Keys;
                this.Manager = (e as Config).Manager;

                var nodes = (e as Config).Nodes;
                var nodeIds = (e as Config).NodeIds;

                this.NumOfIds = (int)Math.Pow(2, nodes.Count);

                for (var idx = 1; idx <= nodes.Count; idx++)
                {
                    var start = (this.NodeId + (int)Math.Pow(2, (idx - 1))) % this.NumOfIds;
                    var end = (this.NodeId + (int)Math.Pow(2, idx)) % this.NumOfIds;

                    var nodeId = this.GetSuccessorNodeId(start, nodeIds);
                    this.FingerTable.Add(start, new Finger(start, end, nodes[nodeId]));
                }

                for (var idx = 0; idx < nodeIds.Count; idx++)
                {
                    if (nodeIds[idx] == this.NodeId)
                    {
                        this.Predecessor = nodes[this.WrapSubtract(idx, 1, nodeIds.Count)];
                        break;
                    }
                }

                this.RaiseEvent(new Local());
            }

            void JoinCluster(Event e)
            {
                this.NodeId = (e as Join).Id;
                this.Manager = (e as Join).Manager;
                this.NumOfIds = (e as Join).NumOfIds;

                var nodes = (e as Join).Nodes;
                var nodeIds = (e as Join).NodeIds;

                for (var idx = 1; idx <= nodes.Count; idx++)
                {
                    var start = (this.NodeId + (int)Math.Pow(2, (idx - 1))) % this.NumOfIds;
                    var end = (this.NodeId + (int)Math.Pow(2, idx)) % this.NumOfIds;

                    var nodeId = this.GetSuccessorNodeId(start, nodeIds);
                    this.FingerTable.Add(start, new Finger(start, end, nodes[nodeId]));
                }

                var successor = this.FingerTable[(this.NodeId + 1) % this.NumOfIds].Node;

                this.SendEvent(this.Manager, new JoinAck());
                this.SendEvent(successor, new NotifySuccessor(this.Id));
            }

            [OnEventDoAction(typeof(FindSuccessor), nameof(ProcessFindSuccessor))]
            [OnEventDoAction(typeof(FindSuccessorResp), nameof(ProcessFindSuccessorResp))]
            [OnEventDoAction(typeof(FindPredecessor), nameof(ProcessFindPredecessor))]
            [OnEventDoAction(typeof(FindPredecessorResp), nameof(ProcessFindPredecessorResp))]
            [OnEventDoAction(typeof(QueryId), nameof(ProcessQueryId))]
            [OnEventDoAction(typeof(AskForKeys), nameof(SendKeys))]
            [OnEventDoAction(typeof(AskForKeysResp), nameof(UpdateKeys))]
            [OnEventDoAction(typeof(NotifySuccessor), nameof(UpdatePredecessor))]
            [OnEventDoAction(typeof(Stabilize), nameof(ProcessStabilize))]
            [OnEventDoAction(typeof(Terminate), nameof(ProcessTerminate))]
            class Waiting : State { }

            void ProcessFindSuccessor(Event e)
            {
                var sender = (e as FindSuccessor).Sender;
                var key = (e as FindSuccessor).Key;
                this.Id.Runtime.Logger.WriteLine($"<ChordLog> '{this.Id}' trying to find successor of key '{key}'");
                if (this.Keys.Contains(key))
                {
                    this.SendEvent(sender, new FindSuccessorResp(this.Id, key));
                }
                else if (this.FingerTable.ContainsKey(key))
                {
                    this.SendEvent(sender, new FindSuccessorResp(this.FingerTable[key].Node, key));
                }
                else if (this.NodeId.Equals(key))
                {
                    this.SendEvent(sender, new FindSuccessorResp(
                        this.FingerTable[(this.NodeId + 1) % this.NumOfIds].Node, key));
                }
                else
                {
                    int idToAsk = -1;
                    foreach (var finger in this.FingerTable)
                    {
                        if (((finger.Value.Start > finger.Value.End) &&
                            (finger.Value.Start <= key || key < finger.Value.End)) ||
                            ((finger.Value.Start < finger.Value.End) &&
                            (finger.Value.Start <= key && key < finger.Value.End)))
                        {
                            idToAsk = finger.Key;
                        }
                    }

                    if (idToAsk < 0)
                    {
                        idToAsk = (this.NodeId + 1) % this.NumOfIds;
                    }

                    if (this.FingerTable[idToAsk].Node.Equals(this.Id))
                    {
                        foreach (var finger in this.FingerTable)
                        {
                            if (finger.Value.End == idToAsk ||
                                finger.Value.End == idToAsk - 1)
                            {
                                idToAsk = finger.Key;
                                break;
                            }
                        }

                        this.Assert(!this.FingerTable[idToAsk].Node.Equals(this.Id),
                            "Cannot locate successor of {0}.", key);
                    }

                    this.SendEvent(this.FingerTable[idToAsk].Node, new FindSuccessor(sender, key));
                }
            }

            void ProcessFindPredecessor(Event e)
            {
                var sender = (e as FindPredecessor).Sender;
                if (this.Predecessor != null)
                {
                    this.SendEvent(sender, new FindPredecessorResp(this.Predecessor));
                }
            }

            void ProcessQueryId(Event e)
            {
                var sender = (e as QueryId).Sender;
                this.SendEvent(sender, new QueryIdResp(this.NodeId));
            }

            void SendKeys(Event e)
            {
                var sender = (e as AskForKeys).Node;
                var senderId = (e as AskForKeys).Id;

                this.Assert(this.Predecessor.Equals(sender), "Predecessor is corrupted.");

                List<int> keysToSend = new List<int>();
                foreach (var key in this.Keys)
                {
                    if (key <= senderId)
                    {
                        keysToSend.Add(key);
                    }
                }

                if (keysToSend.Count > 0)
                {
                    foreach (var key in keysToSend)
                    {
                        this.Keys.Remove(key);
                    }

                    this.SendEvent(sender, new AskForKeysResp(keysToSend));
                }
            }

            void ProcessStabilize()
            {
                var successor = this.FingerTable[(this.NodeId + 1) % this.NumOfIds].Node;
                this.SendEvent(successor, new FindPredecessor(this.Id));

                foreach (var finger in this.FingerTable)
                {
                    if (!finger.Value.Node.Equals(successor))
                    {
                        this.SendEvent(successor, new FindSuccessor(this.Id, finger.Key));
                    }
                }
            }

            void ProcessFindSuccessorResp(Event e)
            {
                var successor = (e as FindSuccessorResp).Node;
                var key = (e as FindSuccessorResp).Key;

                this.Assert(this.FingerTable.ContainsKey(key),
                    "Finger table of {0} does not contain {1}.", this.NodeId, key);
                this.FingerTable[key] = new Finger(this.FingerTable[key].Start,
                    this.FingerTable[key].End, successor);
            }

            void ProcessFindPredecessorResp(Event e)
            {
                var successor = (e as FindPredecessorResp).Node;
                if (!successor.Equals(this.Id))
                {
                    this.FingerTable[(this.NodeId + 1) % this.NumOfIds] =
                        new Finger(this.FingerTable[(this.NodeId + 1) % this.NumOfIds].Start,
                        this.FingerTable[(this.NodeId + 1) % this.NumOfIds].End,
                        successor);

                    this.SendEvent(successor, new NotifySuccessor(this.Id));
                    this.SendEvent(successor, new AskForKeys(this.Id, this.NodeId));
                }
            }

            void UpdatePredecessor(Event e)
            {
                var predecessor = (e as NotifySuccessor).Node;
                if (!predecessor.Equals(this.Id))
                {
                    this.Predecessor = predecessor;
                }
            }

            void UpdateKeys(Event e)
            {
                var keys = (e as AskForKeysResp).Keys;
                foreach (var key in keys)
                {
                    this.Keys.Add(key);
                }
            }

            void ProcessTerminate()
            {
                this.RaiseEvent(HaltEvent.Instance);
            }

            int GetSuccessorNodeId(int start, List<int> nodeIds)
            {
                var candidate = -1;
                foreach (var id in nodeIds.Where(v => v >= start))
                {
                    if (candidate < 0 || id < candidate)
                    {
                        candidate = id;
                    }
                }

                if (candidate < 0)
                {
                    foreach (var id in nodeIds.Where(v => v < start))
                    {
                        if (candidate < 0 || id < candidate)
                        {
                            candidate = id;
                        }
                    }
                }

                for (int idx = 0; idx < nodeIds.Count; idx++)
                {
                    if (nodeIds[idx] == candidate)
                    {
                        candidate = idx;
                        break;
                    }
                }

                return candidate;
            }

            int WrapAdd(int left, int right, int ceiling)
            {
                int result = left + right;
                if (result > ceiling)
                {
                    result = ceiling - result;
                }

                return result;
            }

            int WrapSubtract(int left, int right, int ceiling)
            {
                int result = left - right;
                if (result < 0)
                {
                    result = ceiling + result;
                }

                return result;
            }

            void EmitFingerTableAndKeys()
            {
                this.Id.Runtime.Logger.WriteLine(" ... Printing finger table of node {0}:", this.NodeId);
                foreach (var finger in this.FingerTable)
                {
                    this.Id.Runtime.Logger.WriteLine("  >> " + finger.Key + " | [" + finger.Value.Start +
                        ", " + finger.Value.End + ") | " + finger.Value.Node);
                }

                this.Id.Runtime.Logger.WriteLine(" ... Printing keys of node {0}:", this.NodeId);
                foreach (var key in this.Keys)
                {
                    this.Id.Runtime.Logger.WriteLine("  >> Key-" + key);
                }
            }
        }

        private class Client : StateMachine
        {
            internal class Config : Event
            {
                public ActorId ClusterManager;
                public List<int> Keys;

                public Config(ActorId clusterManager, List<int> keys)
                    : base()
                {
                    this.ClusterManager = clusterManager;
                    this.Keys = keys;
                }
            }

            internal class Local : Event { }

            ActorId ClusterManager;

            List<int> Keys;
            int QueryCounter;

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            [OnEventGotoState(typeof(Local), typeof(Querying))]
            class Init : State { }

            void InitOnEntry(Event e)
            {
                this.ClusterManager = (e as Config).ClusterManager;
                this.Keys = (e as Config).Keys;

                // LIVENESS BUG: can never detect the key, and keeps looping without
                // exiting the process. Enable to introduce the bug.
                this.Keys.Add(17);

                this.QueryCounter = 0;

                this.RaiseEvent(new Local());
            }

            [OnEntry(nameof(QueryingOnEntry))]
            [OnEventGotoState(typeof(Local), typeof(Waiting))]
            class Querying : State { }

            void QueryingOnEntry()
            {
                if (this.QueryCounter < 1)
                {
                    var key = this.GetNextQueryKey();
                    this.Id.Runtime.Logger.WriteLine($"<ChordLog> Client is searching for successor of key '{key}'");
                    this.SendEvent(this.ClusterManager, new ChordNode.FindSuccessor(this.Id, key));
                    this.Monitor<LivenessMonitor>(new LivenessMonitor.NotifyClientRequest(key));

                    this.QueryCounter++;
                }

                this.RaiseEvent(new Local());
            }

            int GetNextQueryKey()
            {
                int keyIndex = -1;
                while (keyIndex < 0)
                {
                    for (int i = 0; i < this.Keys.Count; i++)
                    {
                        if (this.RandomBoolean())
                        {
                            keyIndex = i;
                            break;
                        }
                    }
                }

                return this.Keys[keyIndex];
            }

            [OnEventGotoState(typeof(Local), typeof(Querying))]
            [OnEventDoAction(typeof(ChordNode.FindSuccessorResp), nameof(ProcessFindSuccessorResp))]
            [OnEventDoAction(typeof(ChordNode.QueryIdResp), nameof(ProcessQueryIdResp))]
            class Waiting : State { }

            void ProcessFindSuccessorResp(Event e)
            {
                var successor = (e as ChordNode.FindSuccessorResp).Node;
                var key = (e as ChordNode.FindSuccessorResp).Key;
                this.Monitor<LivenessMonitor>(new LivenessMonitor.NotifyClientResponse(key));
                this.SendEvent(successor, new ChordNode.QueryId(this.Id));
            }

            void ProcessQueryIdResp()
            {
                this.RaiseEvent(new Local());
            }
        }

        private class LivenessMonitor : Monitor
        {
            public class NotifyClientRequest : Event
            {
                public int Key;

                public NotifyClientRequest(int key)
                    : base()
                {
                    this.Key = key;
                }
            }

            public class NotifyClientResponse : Event
            {
                public int Key;

                public NotifyClientResponse(int key)
                    : base()
                {
                    this.Key = key;
                }
            }

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            class Init : State { }

            void InitOnEntry()
            {
                this.RaiseGotoStateEvent<Responded>();
            }

            [Cold]
            [OnEventGotoState(typeof(NotifyClientRequest), typeof(Requested))]
            class Responded : State { }

            [Hot]
            [OnEventGotoState(typeof(NotifyClientResponse), typeof(Responded))]
            class Requested : State { }
        }
    }

    public class Chord
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }

        [Microsoft.Coyote.SystematicTesting.TestAttribute]
        public static void TestChord()
        {
            // CoyoteRuntime.Current.
            // TestFib tf = new TestFib(1, 1, 11);
            // await tf.TestRun();

            var configuration = Configuration.Create().WithQLearningStrategy();

            // Creates a new P# runtime instance, and passes an optional configuration.
            var runtime = Microsoft.Coyote.Actors.RuntimeFactory.Create(configuration);

            ChordTest.Execute(runtime);
        }
    }
}
