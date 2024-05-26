using MongoDB.Bson;
using SayoDeviceStreamingAssistant.Properties;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Windows.Globalization;

namespace SayoDeviceStreamingAssistant.Pages
{
    /// <summary>
    /// Settings.xaml 的交互逻辑
    /// </summary>
    public partial class Settings : Page, IDisposable
    {
        ///public bool ShowUnsupportedDevice => ShowUnsupportedDeviceCheckBox.IsChecked ?? true;
        private readonly BsonDocument settings;
        public Settings()
        {
            InitializeComponent();
            var languages = new[]
            {
                new {Name = "Auto"   , Value = "auto"},
                new {Name = "English", Value = "en"},
                new {Name = "简体中文", Value = "zh"}
            };
            LanguageComboBox.ItemsSource = languages;
            try
            {
                settings = BsonDocument.Parse(File.ReadAllText("./settings.json"));
                LanguageComboBox.SelectedValue = languages[Array.FindIndex(languages, l => l.Value == settings["language"].AsString)];
                //ShowUnsupportedDeviceCheckBox.IsChecked = settings["showUnsupportedDevice"].AsBoolean;
            }catch (Exception)
            {
                settings = new BsonDocument
                {
                    {"language", "auto"},
                    //{"showUnsupportedDevice", true}
                };
            }
        }
        public void Dispose()
        {
            settings["language"] = (LanguageComboBox.SelectedValue as dynamic).Value;
            File.WriteAllText("./settings.json", settings.ToJson());
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem == null) return;
            var selectedLanguage = (LanguageComboBox.SelectedItem as dynamic).Value;
            var isLanguageChanged = settings["language"].AsString != selectedLanguage;

            if (selectedLanguage == "auto") {
                RestartAppLabel.Content = Properties.Resources.ResourceManager
                    .GetString("RestartAppLabel.Content");
                RestartAppButton.Content = Properties.Resources.ResourceManager
                    .GetString("RestartAppButton.Content");
            }
            else {
                RestartAppLabel.Content = Properties.Resources.ResourceManager
                    .GetString("RestartAppLabel.Content", CultureInfo.GetCultureInfo(selectedLanguage));
                RestartAppButton.Content = Properties.Resources.ResourceManager
                    .GetString("RestartAppButton.Content", CultureInfo.GetCultureInfo(selectedLanguage));
            }
            
            var anim = new DoubleAnimation
            {
                From = isLanguageChanged ? 0 : 40,
                To = isLanguageChanged ? 40 : 0,
                Duration = TimeSpan.FromSeconds(0.2)
            };
            RestartAppBar.BeginAnimation(HeightProperty, anim);
        }

        private void ShowUnsupportedDeviceCheckBox_Click(object sender, RoutedEventArgs e)
        {
            //settings["showUnsupportedDevice"] = ShowUnsupportedDeviceCheckBox.IsChecked;
        }

        private void RestartAppButton_Click(object sender, RoutedEventArgs e)
        {
            Dispose();
            App.RestartApp();
        }
    }
}
