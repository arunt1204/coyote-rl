using Microsoft.Coyote;
using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.Tasks;
using System;
using System.Collections.Generic;

namespace Benchmarks
{
    public class SafeStack
    {
        private static IActorRuntime Runtime;

        private struct SafeStackItem
        {
            public int Value;
            public volatile int Next;
        }

        private class Stack
        {
            internal readonly SafeStackItem[] Array;
            internal volatile int Head;
            internal volatile int Count;

            private readonly AsyncLock ArrayLock;
            private readonly AsyncLock HeadLock;
            private readonly AsyncLock CountLock;

            public Stack(int pushCount)
            {
                this.Array = new SafeStackItem[pushCount];
                this.Head = 0;
                this.Count = pushCount;

                for (int i = 0; i < pushCount - 1; i++)
                {
                    this.Array[i].Next = i + 1;
                }

                this.Array[pushCount - 1].Next = -1;

                this.ArrayLock = AsyncLock.Create();
                this.HeadLock = AsyncLock.Create();
                this.CountLock = AsyncLock.Create();

                Runtime.Monitor<StateMonitor>(new StateMonitor.UpdateStateEvent(this.Array));
            }

            public async Task PushAsync(int id, int index)
            {
                Runtime.Logger.WriteLine($"Task {id} starts push {index}.");
                Task.ExploreContextSwitch();
                int head = this.Head;
                Runtime.Logger.WriteLine($"Task {id} reads head {head} in push {index}.");
                bool compareExchangeResult = false;

                do
                {
                    Task.ExploreContextSwitch();
                    this.Array[index].Next = head;
                    Runtime.Logger.WriteLine($"Task {id} sets [{index}].next to {head} during push.");
                    Runtime.Monitor<StateMonitor>(new StateMonitor.UpdateStateEvent(this.Array));

                    Task.ExploreContextSwitch();
                    using (await this.HeadLock.AcquireAsync())
                    {
                        if (this.Head == head)
                        {
                            this.Head = index;
                            compareExchangeResult = true;
                            Runtime.Logger.WriteLine($"Task {id} compare-exchange in push {index} succeeded (head = {this.Head}, count = {this.Count}).");
                            Runtime.Monitor<StateMonitor>(new StateMonitor.UpdateStateEvent(this.Array));
                        }
                        else
                        {
                            head = this.Head;
                            Runtime.Logger.WriteLine($"Task {id} compare-exchange in push {index} failed and re-read head {head}.");
                        }
                    }
                }
                while (!compareExchangeResult);

                Task.ExploreContextSwitch();
                using (await this.CountLock.AcquireAsync())
                {
                    this.Count++;
                    Runtime.Monitor<StateMonitor>(new StateMonitor.UpdateStateEvent(this.Array));
                }

                Runtime.Logger.WriteLine($"Task {id} pushed {index} (head = {this.Head}, count = {this.Count}).");
                Runtime.Logger.WriteLine($"   [0] = {this.Array[0]} | next = {this.Array[0].Next}");
                Runtime.Logger.WriteLine($"   [1] = {this.Array[1]} | next = {this.Array[1].Next}");
                Runtime.Logger.WriteLine($"   [2] = {this.Array[2]} | next = {this.Array[2].Next}");
                Runtime.Logger.WriteLine($"");
            }

            public async Task<int> PopAsync(int id)
            {
                Runtime.Logger.WriteLine($"Task {id} starts pop.");
                while (this.Count > 1)
                {
                    Task.ExploreContextSwitch();
                    int head = this.Head;
                    Runtime.Logger.WriteLine($"Task {id} reads head {head} in pop ([{head}].next is {this.Array[head].Next}).");

                    int next;
                    Task.ExploreContextSwitch();
                    using (await this.ArrayLock.AcquireAsync())
                    {
                        next = this.Array[head].Next;
                        this.Array[head].Next = -1;
                        Runtime.Logger.WriteLine($"Task {id} exchanges {next} from [{head}].next with -1.");
                        Runtime.Monitor<StateMonitor>(new StateMonitor.UpdateStateEvent(this.Array));
                    }

                    Task.ExploreContextSwitch();
                    int headTemp = head;
                    bool compareExchangeResult = false;
                    Task.ExploreContextSwitch();
                    using (await this.HeadLock.AcquireAsync())
                    {
                        if (this.Head == headTemp)
                        {
                            this.Head = next;
                            compareExchangeResult = true;
                            Runtime.Logger.WriteLine($"Task {id} compare-exchange in pop succeeded (head = {this.Head}, count = {this.Count}).");
                            Runtime.Monitor<StateMonitor>(new StateMonitor.UpdateStateEvent(this.Array));
                        }
                        else
                        {
                            headTemp = this.Head;
                            Runtime.Logger.WriteLine($"Task {id} compare-exchange in pop failed and re-read head {headTemp}.");
                        }
                    }

                    if (compareExchangeResult)
                    {
                        Task.ExploreContextSwitch();
                        using (await this.CountLock.AcquireAsync())
                        {
                            this.Count--;
                            Runtime.Monitor<StateMonitor>(new StateMonitor.UpdateStateEvent(this.Array));
                        }

                        Runtime.Logger.WriteLine($"Task {id} pops {head} (head = {this.Head}, count = {this.Count}).");
                        Runtime.Logger.WriteLine($"   [0] = {this.Array[0]} | next = {this.Array[0].Next}");
                        Runtime.Logger.WriteLine($"   [1] = {this.Array[1]} | next = {this.Array[1].Next}");
                        Runtime.Logger.WriteLine($"   [2] = {this.Array[2]} | next = {this.Array[2].Next}");
                        Runtime.Logger.WriteLine($"");
                        return head;
                    }
                    else
                    {
                        Task.ExploreContextSwitch();
                        using (await this.ArrayLock.AcquireAsync())
                        {
                            this.Array[head].Next = next;
                            Runtime.Monitor<StateMonitor>(new StateMonitor.UpdateStateEvent(this.Array));
                        }
                    }
                }

                return -1;
            }
        }

        public async Task Run(IActorRuntime runtime)
        {
            runtime.RegisterMonitor<StateMonitor>();

            Runtime = runtime;

            int numTasks = 5;
            var stack = new Stack(numTasks);

            Task[] tasks = new Task[numTasks];
            for (int i = 0; i < numTasks; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    int id = i;
                    Runtime.Logger.WriteLine($"Starting task {id}.");
                    for (int j = 0; j != 2; j += 1)
                    {
                        int elem;
                        while (true)
                        {
                            elem = await stack.PopAsync(id);
                            if (elem >= 0)
                            {
                                break;
                            }

                            Task.ExploreContextSwitch();
                        }

                        stack.Array[elem].Value = id;
                        Runtime.Logger.WriteLine($"Task {id} popped item '{elem}' and writes value '{id}'.");
                        Runtime.Monitor<StateMonitor>(new StateMonitor.UpdateStateEvent(stack.Array));
                        Task.ExploreContextSwitch();
                        Specification.Assert(stack.Array[elem].Value == id,
                            $"Task {id} found bug: [{elem}].{stack.Array[elem].Value} is not '{id}'!");
                        await stack.PushAsync(id, elem);
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        private class StateMonitor : Monitor
        {
            internal class UpdateStateEvent : Event
            {
                internal readonly SafeStackItem[] Array;

                internal UpdateStateEvent(SafeStackItem[] array)
                {
                    this.Array = array;
                }
            }

            public SafeStackItem[] Array;

            protected override int HashedState
            {
                get
                {
                    unchecked
                    {
                        int hash = 37;
                        foreach (var item in this.Array)
                        {
                            int arrayHash = 37;
                            arrayHash = (arrayHash * 397) + item.Value.GetHashCode();
                            arrayHash = (arrayHash * 397) + item.Next.GetHashCode();
                            hash *= arrayHash;
                        }

                        return hash;
                    }
                }
            }

            [Start]
            [OnEventDoAction(typeof(UpdateStateEvent), nameof(UpdateState))]
            class Init : State { }

            void UpdateState(Event e)
            {
                var array = (e as UpdateStateEvent).Array;
                this.Array = array;
            }
        }
    }
}
