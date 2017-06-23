using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PipeApp
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private NamedPipeServerStream pipe;
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            pipe = new NamedPipeServerStream("mypipe", PipeDirection.InOut, -1, PipeTransmissionMode.Byte,
                PipeOptions.None, 0, 0, null, HandleInheritability.None, PipeAccessRights.ChangePermissions);
            PipeSecurity ps = pipe.GetAccessControl();
            PipeAccessRule clientRule = new PipeAccessRule(
                new SecurityIdentifier("S-1-15-2-1"), // All application packages
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow);
            PipeAccessRule ownerRule = new PipeAccessRule(WindowsIdentity.GetCurrent().Owner,
                PipeAccessRights.FullControl,
                AccessControlType.Allow);
            ps.AddAccessRule(clientRule);
            ps.AddAccessRule(ownerRule);
            pipe.SetAccessControl(ps);
            pipe.WaitForConnection();
            using (var sr = new StreamReader(pipe, Encoding.UTF8))
            {
                while (true)
                {
                    string message = sr.ReadLine();
                    Debug.WriteLine(message);
                    //在此处处理App写入命名管道的内容
                    pipe.WaitForPipeDrain();
                }
            }
        }
    }
}
