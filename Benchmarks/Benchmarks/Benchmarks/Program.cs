using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Tasks;
using System;
using System.Collections.Generic;

namespace Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static void TestChord(IActorRuntime runtime)
        {
            Chord.Execute(runtime);
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static void TestRaft_v1(IActorRuntime runtime)
        {
            Raft.Execute(runtime, 4);
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static void TestRaft_v2(IActorRuntime runtime)
        {
            Raft.Execute(runtime, 6);
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static void TestFD(IActorRuntime runtime)
        {
            FailureDetector.Execute(runtime);
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static void TestPaxos(IActorRuntime runtime)
        {
            Paxos.Execute(runtime);
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static async Task Test_Tri_Bench_2()
        {
            Traingular tt = new Traingular(3, 6, 5);
            await tt.TestRun();
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static async Task Test_Large_Tri_Bench_2()
        {
            Traingular tt = new Traingular(3, 6, 20);
            await tt.TestRun();
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static async Task Test_Fib_Bench_2()
        {
            Fib tf = new Fib(1, 1, 5);
            await tf.TestRun();
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static async Task Test_Large_Fib_Bench_2()
        {
            Fib tf = new Fib(1, 1, 11);
            await tf.TestRun();
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static async Task Test_SafeStack(IActorRuntime runtime)
        {
            SafeStack ss = new SafeStack();
            await ss.Run(runtime);
        }

        [Microsoft.Coyote.SystematicTesting.Test]
        public static void Test_BoundedBuffer()
        {
            BoundedBuffer buffer = new BoundedBuffer(false);
            var tasks = new List<Task>
                {
                    Task.Run(() => BoundedBuffer.Reader(buffer)),
                    Task.Run(() => BoundedBuffer.Reader(buffer)),
                    Task.Run(() => BoundedBuffer.Writer(buffer))
                };

            Task.WaitAll(tasks.ToArray());
        }
    }
}
