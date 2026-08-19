using System.Reflection;
using System.Threading;
using System.Windows.Controls;
using SnookerPoint.App.Controls;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Directly exercises the credential control that was broken: keystrokes must reach
/// the authoritative <see cref="CredentialBox.Password"/>, and toggling reveal must
/// preserve the value. WPF controls require an STA thread.
/// </summary>
[Collection("WpfSmoke")]
public class CredentialBoxTests
{
    private static void RunSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured is not null)
        {
            throw captured;
        }
    }

    private static PasswordBox InnerPasswordBox(CredentialBox c) =>
        (PasswordBox)typeof(CredentialBox)
            .GetField("Pb", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(c)!;

    private static TextBox InnerTextBox(CredentialBox c) =>
        (TextBox)typeof(CredentialBox)
            .GetField("Tb", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(c)!;

    [Fact]
    public void TypingInMaskedBox_ReachesThePasswordProperty()
    {
        RunSta(() =>
        {
            var box = new CredentialBox();
            InnerPasswordBox(box).Password = "typed123"; // simulates user typing

            Assert.Equal("typed123", box.Password);
        });
    }

    [Fact]
    public void RevealingValue_PreservesIt_AndSyncsPlainTextBox()
    {
        RunSta(() =>
        {
            var box = new CredentialBox();
            InnerPasswordBox(box).Password = "secret123";

            box.Reveal = true;

            Assert.Equal("secret123", box.Password);            // value preserved
            Assert.Equal("secret123", InnerTextBox(box).Text);  // visible control shows it
        });
    }

    [Fact]
    public void TypingInRevealedBox_ReachesThePasswordProperty_AndSyncsMaskedBox()
    {
        RunSta(() =>
        {
            var box = new CredentialBox { Reveal = true };
            InnerTextBox(box).Text = "plain456";

            Assert.Equal("plain456", box.Password);
            Assert.Equal("plain456", InnerPasswordBox(box).Password);
        });
    }

    [Fact]
    public void SettingPasswordFromViewModel_UpdatesBothInnerControls()
    {
        RunSta(() =>
        {
            var box = new CredentialBox();
            box.Password = "fromvm";

            Assert.Equal("fromvm", InnerPasswordBox(box).Password);
            Assert.Equal("fromvm", InnerTextBox(box).Text);
        });
    }

    [Fact]
    public void TogglingRevealRepeatedly_NeverLosesValue()
    {
        RunSta(() =>
        {
            var box = new CredentialBox();
            InnerPasswordBox(box).Password = "keepme";

            box.Reveal = true;
            box.Reveal = false;
            box.Reveal = true;

            Assert.Equal("keepme", box.Password);
            Assert.Equal("keepme", InnerTextBox(box).Text);
        });
    }
}
