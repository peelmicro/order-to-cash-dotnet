using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// Backlog id 85, bullet 4 — the change of KIND, against a REAL Docker
/// daemon, in the project where the race was first measured (57 of 59 tests
/// dead at 1 ms out of <c>KafkaContainerFixture.InitializeAsync</c>,
/// <c>Bind for 0.0.0.0:35127 failed: port is already allocated</c>).
///
/// <para>
/// The two cases are the same experiment with one variable changed. Both
/// bind a real container port; both run with the port the retired shape
/// would have chosen already taken by another socket, which is what
/// "someone took it in the window" means. The RETIRED shape fails every
/// time and the ADOPTED shape succeeds every time — not more often, every
/// time — because the adopted shape never has a window: Docker chooses the
/// host port while it holds it.
/// </para>
///
/// <para>
/// The determinism is in the TEST, not in the thing under test: the
/// squatting listener is held open across the whole container start rather
/// than being closed first, so the retired shape cannot win by being fast.
/// Nothing here waits on a probability.
/// </para>
///
/// <para>
/// <c>nats:2.14.5-alpine</c> is used because it is already pulled by five
/// fixtures in this repository and starts in about a second; the race is a
/// property of Docker's port binding, not of any particular image. The
/// image, command and wait strategy are copied from this project's own
/// <c>TestSupport/NatsContainerFixture</c> so the adopted arm is the real
/// in-tree control rather than a simplified stand-in.
/// </para>
/// </summary>
public sealed class ContainerHostPortAssignmentRaceTests
{
    private const string Image = "nats:2.14.5-alpine";
    private const int NatsClientPort = 4222;

    /// <summary>Every arm runs three times; "every time" is a claim about repetitions, so it is made over repetitions.</summary>
    private const int Repetitions = 3;

    /// <summary>How many ports the adopted arm keeps held while it starts a container — the retired shape would have picked one of these.</summary>
    private const int PortsHeldAgainstTheAdoptedArm = 16;

    [Fact]
    public async Task Id85_TheRetiredSelfAssignedShape_FailsEveryTime_WhenThePortIsTakenInsideItsOwnCheckToBindWindow()
    {
        var outcomes = new List<string>();

        for (var repetition = 1; repetition <= Repetitions; repetition++)
        {
            // The retired shape's own two steps, verbatim: choose a port the
            // OS says is free, and let go of it.
            var chosenPort = ChoosePortTheRetiredWay();

            // The window. In production this is another process getting there
            // first; here it is this test, deterministically.
            using var squatter = HoldThePortForTheWholeOfTheWindow(chosenPort);

            var container = BuildWithASelfAssignedHostPort(chosenPort);
            var thrown = await Record.ExceptionAsync(() => container.StartAsync());
            await container.DisposeAsync();

            outcomes.Add(thrown is null
                ? $"repetition {repetition}: host port {chosenPort} — STARTED, no exception"
                : $"repetition {repetition}: host port {chosenPort} — {thrown.GetType().Name}: {Flatten(thrown)}");

            Assert.True(
                thrown is not null,
                $"Backlog id 85 — the retired self-assigned shape was expected to LOSE the race every time and did not. "
                + $"Host port {chosenPort} was chosen by a TcpListener that was then closed, another socket took it before "
                + $"the container started, and Docker started the container anyway. Outcomes so far: "
                + string.Join(" | ", outcomes));

            Assert.True(
                Flatten(thrown!).Contains(chosenPort.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal),
                $"Backlog id 85 — the retired shape failed, but not for the reason this case is about: Docker's error does "
                + $"not mention host port {chosenPort}, so this is not the 'port is already allocated' / 'address already "
                + $"in use' failure the race produces. The error was: {Flatten(thrown!)}");
        }
    }

    [Fact]
    public async Task Id85_TheAdoptedDockerAssignedShape_StartsEveryTime_EvenWhileEveryPortTheRetiredShapeWouldHaveChosenIsHeld()
    {
        var squatters = new List<TcpListener>();
        var heldPorts = new List<int>();

        try
        {
            for (var i = 0; i < PortsHeldAgainstTheAdoptedArm; i++)
            {
                var port = ChoosePortTheRetiredWay();
                squatters.Add(HoldThePortForTheWholeOfTheWindow(port));
                heldPorts.Add(port);
            }

            for (var repetition = 1; repetition <= Repetitions; repetition++)
            {
                var container = BuildLettingDockerAssignTheHostPort();

                try
                {
                    var thrown = await Record.ExceptionAsync(() => container.StartAsync());

                    Assert.True(
                        thrown is null,
                        $"Backlog id 85 — the adopted shape (WithPortBinding({NatsClientPort}, true), Docker assigns and "
                        + $"holds the host port) was expected to start every time and failed on repetition {repetition} "
                        + $"while these ports were held: [{string.Join(", ", heldPorts)}]. The error was: {Flatten(thrown!)}");

                    var mapped = container.GetMappedPublicPort(NatsClientPort);

                    Assert.True(
                        !heldPorts.Contains(mapped),
                        $"Backlog id 85 — Docker reported host port {mapped} for the adopted shape, but that port is one "
                        + $"this test is holding open: [{string.Join(", ", heldPorts)}]. Docker cannot have bound it, so "
                        + "the port read back is not the port in use.");
                }
                finally
                {
                    await container.DisposeAsync();
                }
            }
        }
        finally
        {
            foreach (var squatter in squatters)
            {
                squatter.Stop();
                squatter.Dispose();
            }
        }
    }

    /// <summary>
    /// The retired shape's own port choice, reproduced here on purpose:
    /// open a listener on port 0, read what the OS assigned, and CLOSE it.
    /// This is the only remaining copy of that code in the repository
    /// outside <c>SendFailureClassifierRealSmtpTests.GetUnboundPort</c>, and
    /// <c>ContainerFixtureHostPortAssignmentTests</c> names it explicitly so
    /// that it cannot quietly spread back into a fixture.
    /// </summary>
    private static int ChoosePortTheRetiredWay()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// The adversary. Held for the whole container start rather than closed
    /// again, which is what makes both arms deterministic — the retired
    /// shape cannot win by racing, and the adopted shape must demonstrably
    /// avoid every port that is taken.
    /// </summary>
    private static TcpListener HoldThePortForTheWholeOfTheWindow(int port)
    {
        var squatter = new TcpListener(IPAddress.Loopback, port);
        squatter.Start();
        return squatter;
    }

    private static IContainer BuildWithASelfAssignedHostPort(int hostPort) =>
        new ContainerBuilder(Image)
            .WithPortBinding(hostPort, NatsClientPort)
            .WithCommand("-p", NatsClientPort.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
            .Build();

    private static IContainer BuildLettingDockerAssignTheHostPort() =>
        new ContainerBuilder(Image)
            .WithPortBinding(NatsClientPort, true)
            .WithCommand("-p", NatsClientPort.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
            .Build();

    /// <summary>
    /// Docker's own wording moved between daemon versions — the first
    /// sighting recorded <c>Bind for 0.0.0.0:35127 failed: port is already
    /// allocated</c>, and Docker 29.8.0 on this machine says <c>failed to
    /// bind host port 0.0.0.0:NNNNN/tcp: address already in use</c>. Both
    /// name the port, so the port number is what this test asserts on; the
    /// whole exception chain is flattened because Testcontainers wraps the
    /// daemon's message.
    /// </summary>
    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" -> ", messages);
    }
}
