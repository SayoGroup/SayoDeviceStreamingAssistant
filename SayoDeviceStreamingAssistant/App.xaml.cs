using MongoDB.Bson;
using System;
using System.Globalization;
using System.IO;
using System.Windows;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App {
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);
            try
            {
                var settings = BsonDocument.Parse(File.ReadAllText("./settings.json"));
                var culture = settings["language"].AsString;
                SayoDeviceStreamingAssistant.Properties.Resources.Culture
                    = CultureInfo.GetCultureInfoByIetfLanguageTag(culture);
            } catch (Exception)
            {
                // ignored
            }
        }
        
        public static void RestartApp() {
            System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
            Application.Current.Shutdown();
        }
        
    }
}
