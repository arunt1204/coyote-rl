## Test concurrent CRUD operations with Coyote

In the previous [tutorial](first-concurrency-unit-test.md), we explored how to write our first
_concurrency unit test_ for `AccountManager`, and reliably reproduce a race condition in the
`CreateAccount` logic using Coyote. As a typical resource controller, `AccountManager` exposes an API
for creating, getting and deleting accounts. Such operations (including updating, which we skip here
for simplicity) are commonly known as [CRUD](https://en.wikipedia.org/wiki/CRUD).

In this follow-up tutorial, we will write a few more tests that exercise the concurrency in the
`AccountManager` CRUD operations.

## What you will need

To run the `AccountManager` example, you will need to:

- Install [Visual Studio 2019](https://visualstudio.microsoft.com/downloads/).
- Install the [.NET 5.0 version of the coyote tool](../get-started/install.md).
- Be familiar with the `coyote test` tool. See [Testing](../tools/testing.md).
- Be familiar with the `coyote rewrite` tool. See [Rewriting](../tools/rewriting.md).

## Walkthrough

Our [first concurrency unit test](first-concurrency-unit-test.md), `TestConcurrentAccountCreation`,
exercised a race between two concurrent `CreateAccount` requests. A logical next test would be to
create two `DeleteAccount` requests and see what happens whey execute concurrently (as if the system
was running in production). Let's write this test.

```c#
[Test]
public static async Task TestConcurrentAccountDeletion()
{
  // Initialize the mock in-memory DB and account manager.
  var dbCollection = new InMemoryDbCollection();
  var accountManager = new AccountManager(dbCollection);

  // Create some dummy data.
  string accountName = "MyAccount";
  string accountPayload = "...";

  // Create the account and wait for it to complete.
  await accountManager.CreateAccount(accountName, accountPayload);

  // Call DeleteAccount twice without awaiting, which makes both methods run
  // asynchronously with each other.
  var task1 = accountManager.DeleteAccount(accountName);
  var task2 = accountManager.DeleteAccount(accountName);

  // Then wait both requests to complete.
  await Task.WhenAll(task1, task2);

  // Finally, assert that only one of the two requests succeeded and the other
  // failed. Note that we do not know which one of the two succeeded as the
  // requests ran concurrently (this is why we use an exclusive OR).
  Assert.True(task1.Result ^ task2.Result);
}
```

The test looks pretty similar to the `TestConcurrentAccountCreation` from the previous tutorial. The
main difference is that it first runs a `CreateAccount` request and waits it to complete, because
without first creating an account we cannot test its deletion! The test then invokes two
`DeleteAccount` requests, without waiting so that they can run at the same time, then waits for both
to complete before finally asserting that only one deletion happened (and the other was ignored).

Let's run this `TestConcurrentAccountDeletion` test using Coyote (remember to use `coyote rewrite`
first!):

```plain
coyote rewrite .\AccountManager.dll
coyote test .\AccountManager.dll -m TestConcurrentAccountDeletion -i 100
```

When you run this `TestConcurrentAccountDeletion` test using Coyote, you'll find that Coyote finds
a bug very fast. This bug is caused due to a very similar race condition that made the
`TestConcurrentAccountCreation` test fail.

```plain
. Testing .\AccountManager.dll
... Method TestConcurrentAccountDeletion
... Started the testing task scheduler (process:15672).
... Created '1' testing task (process:15672).
... Task 0 is using 'random' strategy (seed:3336165456).
..... Iteration #1
..... Iteration #2
..... Iteration #3
... Task 0 found a bug.
... Emitting task 0 traces:
..... Writing AccountManager_0_5.txt
..... Writing AccountManager_0_5.schedule
... Elapsed 0.1939681 sec.
... Testing statistics:
..... Found 1 bug.
... Scheduling statistics:
..... Explored 3 schedules: 3 fair and 0 unfair.
..... Found 33.33% buggy schedules.
..... Number of scheduling points in fair terminating schedules: 24 (min), 27 (avg), 31 (max).
... Elapsed 0.444569 sec.
```

Both concurrent `DeleteAccount` requests conclude that the account exists, before either of them
gets a chance to delete the row. Then due to the race condition, both requests proceed to delete the
row with one succeeding and the other failing with a `RowNotFoundException` exception.

```C#
// Returns true if the account is deleted, else false.
public async Task<bool> DeleteAccount(string accountName)
{
  if (!await this.AccountCollection.DoesRowExist(accountName))
  {
    return false;
  }

  await this.AccountCollection.DeleteRow(accountName);
  return true;
}
```

This exception is a bug that could possibly lead to unintended consequences in your logic (e.g. data
corruption or loss). It could also trigger a [500 internal server
error](https://httpstatuses.com/500), if `AccountManager` was a deployed service, which is not a
nice HTTP status code to return to the client of the request!

Here's one way to fix the `DeleteAccount` method:

```c#
// Returns true if the account is deleted, else false.
public async Task<bool> DeleteAccount(string userName)
{
  try
  {
    await this.AccountCollection.DeleteRow(userName);
    return true;
  }
  catch (RowNotFoundException)
  {
    return false;
  }
}
```

The above implementation avoids using the `DoesRowExist` method, because we can never be sure that
some other concurrent request did not already delete the same row while this method is executing
between the `DoesRowExist` and  `DeleteRow` calls. Such race conditions might not happen _most_ of
the times, but clearly _some_ times they can happen. In production, with thousands of requests
happening at the same time, such race conditions might manifest more often and are notoriously hard
to reproduce and debug. This is why using Coyote can really help.

Going back to the fixed method above, it directly calls `DeleteRow` on `IDbCollection` and relies on
the database returning an error response (through the `RowNotFound` exception) to deal with such
race conditions. It effectively offloads the problem of preventing such races to the database
itself. Trying to do this in the application logic would be really hard.

So far we wrote two concurrency unit tests, one exercising two concurrent `CreateAccount` requests
and the other one exercising two concurrent `DeleteAccount` requests. Can you think of other
interesting test cases that would allow us to test the CRUD operations of `AccountManager` more
thoroughly? Any set of methods that may read or write shared state (and thus potentially create
interference) are a great candidate for writing concurrency unit tests and test them with Coyote.

What about a race between a `CreateAccount` and `DeleteAccount` call?

At first glance, this may seem like a weird test to write as we can't control the order in which the
two methods will concurrently execute in respect with each other. This makes it difficult to write
meaningful assertions at the end of the test. But if you think about this more deeply, you will
realize that there are only a few distinct outcomes of these races and we can assert for each of
these outcomes. Tests like these are very useful for ensuring that when CRUD operations execute
concurrently (which is the norm in production!), a resource (an account in this case) is either
fully created or fully deleted, and is not left in a half-broken state. This particular example is
simple but you can easily imagine more complex examples where such race conditions can in fact lead
to your state being corrupted (e.g. partially created and partially deleted) in the presence of
concurrency bugs.

Coyote allows you to easily test exactly these kinds of scenarios and making sure that your customer
data will not get corrupted. The awesome part is that Coyote will find and help you debug these bugs
on your local machine before you deploy your service, saving you ton of headache! Of course this
does not only apply to CRUD operations and resource providers, you can imagine many other systems
can benefit of this kind of testing!

Going back to our `AccountManager` example, let's write this test that exercises `CreateAccount` and
`DeleteAccount` requests that happen at the same time. The test is a bit more involved this time.

```c#
[Test]
public static async Task TestConcurrentAccountCreationAndDeletion()
{
  // Initialize the mock in-memory DB and account manager.
  var dbCollection = new InMemoryDbCollection();
  var accountManager = new AccountManager(dbCollection);

  // Create some dummy data.
  string accountName = "MyAccount";
  string accountPayload = "...";

  // Call CreateAccount and DeleteAccount without awaiting, which makes both
  // methods run asynchronously with each other.
  var createTask = accountManager.CreateAccount(accountName, accountPayload);
  var deleteTask = accountManager.DeleteAccount(accountName);

  // Then wait both requests to complete.
  await Task.WhenAll(createTask, deleteTask);

  // The CreateAccount request will always succeed, no matter what. The DeleteAccount
  // may or may not succeed depending on if it finds the account already created or
  // not created while executing concurrently with the CreateAccount request.
  Assert.True(createTask.Result);

  if (!deleteTask.Result)
  {
    // The DeleteAccount request didn't find the account and failed as expected.
    // We assert that the account payload is still available.
    string fetchedAccountPayload = await accountManager.GetAccount(accountName);
    Assert.Equal(accountPayload, fetchedAccountPayload);
  }
  else
  {
    // If CreateAccount and DeleteAccount both returned true, then the account
    // must have been created before the deletion happened.
    // We assert that the payload is not available, as the account was deleted.
    string fetchedAccountPayload = await accountManager.GetAccount(accountName);
    Assert.Null(fetchedAccountPayload);
  }
}
```

Let's run our `TestConcurrentAccountCreationAndDeletion` test with Coyote (remember to rewrite
the assembly first!) and see if it passes.

```plain
coyote rewrite .\AccountManager.dll
coyote test .\AccountManager.dll -m TestConcurrentAccountCreationAndDeletion -i 100
```

The test passed without reporting any bugs which gives us confidence in the correctness of our code.

```plain
. Testing .\AccountManager.dll
... Method TestConcurrentAccountCreationAndDeletion
... Started the testing task scheduler (process:25840).
... Created '1' testing task (process:25840).
... Task 0 is using 'random' strategy (seed:2968084874).
..... Iteration #1
..... Iteration #2
...
..... Iteration #100
... Testing statistics:
..... Found 0 bugs.
... Scheduling statistics:
..... Explored 100 schedules: 100 fair and 0 unfair.
..... Number of scheduling points in fair terminating schedules: 12 (min), 23 (avg), 35 (max).
... Elapsed 0.2443369 sec.
```

Often times, even if your code is correct and Coyote doesn't find any bugs, it makes you deeply
think about possible scenarios and outcomes in the presence of race conditions in your logic, and
helps you reason about the correct behavior. We believe that this clarity in thinking alone is
extremely valuable. Developers often reason about edge cases when writing sequential tests, but
sometimes miss bugs in the presence of concurrency as they don't write focused concurrency unit
tests, as they are impractical to write without a tool like Coyote.

Now this was a simple example but you can imagine how it would be possible for the `AccountManager`
to get into an inconsistent state if the `CreateAccount` and `DeleteAccount` methods involved
multiple steps, such as modifying a couple of collections instead of just one, emitting events and
create entries in other non-database systems (e.g. provisioning a dedicated Azure Storage container
for the account).

## Get the sample source code

To get the complete source code for the `AccountManager` tutorial, clone the
[Coyote Samples git repo](http://github.com/microsoft/coyote-samples).

You can build the sample by running the following command:

```plain
powershell -f build.ps1
```

To rewrite and test the sample with Coyote you can use the following commands (as discussed above):

```plain
coyote rewrite .\AccountManager.dll
coyote test .\AccountManager.dll -m TestConcurrentAccountDeletion -i 100
```

If you find a bug you can replay with the following command:
```plain
coyote replay .\AccountManager.dll -schedule AccountManager_0_0.schedule -m TestConcurrentAccountDeletion
```

Feel free to play around with the code and write more concurrency unit tests with Coyote!
