using System;
using System.Collections.Generic;
using System.Linq;
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

namespace CalcFittingsPlugin
{
    /// <summary>
    /// Логика взаимодействия для UserControl3.xaml
    /// </summary>
    public partial class UserControl3 : Window
    {
        public UserControl3(string lvlName)
        {
            InitializeComponent();
            LvlTextBox.Text = lvlName;
        }

        private void Delete3DButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Delete2DButton_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
