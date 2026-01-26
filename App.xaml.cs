using System.Configuration;
using System.Data;
using System.Windows;

namespace Music
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static DiscordPresence? DiscordPresence { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DiscordPresence = new DiscordPresence();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DiscordPresence?.Dispose();
            base.OnExit(e);
        }
    }

}
