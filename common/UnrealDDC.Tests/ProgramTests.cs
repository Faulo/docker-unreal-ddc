using System;
using NUnit.Framework;

namespace UnrealDDC.Tests;

public sealed class ProgramTests {
    [TestCase("serve", "SERVE")]
    [TestCase("health", "HEALTH")]
    [TestCase("version", "VERSION")]
    public void ParsesExactLauncherCommands(string argument, string expected) {
        Assert.That(Program.ParseCommand([argument]).ToString(), Is.EqualTo(expected));
    }

    [TestCase]
    [TestCase("serve", "--extra")]
    [TestCase("--health")]
    [TestCase("--launcher-version")]
    [TestCase("unknown")]
    public void RejectsMissingExtraAndLegacyArguments(params string[] arguments) {
        Assert.That(
            () => Program.ParseCommand(arguments),
            Throws.TypeOf<InvalidOperationException>()
        );
    }
}
