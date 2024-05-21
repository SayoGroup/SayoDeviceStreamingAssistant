using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// SourcesManagePage.xaml 的交互逻辑
    /// </summary>
    public partial class SourcesManagePage : Page {
        public static ObservableCollection<FrameSource> FrameSources = new ObservableCollection<FrameSource>();
        public SourcesManagePage() {
            InitializeComponent();
        }
        private bool isFrameVisible = false;

        private void BackButton_Click(object sender, RoutedEventArgs e) {
            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.HideSourcesManagePage();
        }
    }
}
