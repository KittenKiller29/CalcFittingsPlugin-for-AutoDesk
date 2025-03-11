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
    /// Логика взаимодействия для UserControl2.xaml
    /// </summary>
    public partial class UserControl2 : Window
    {
        //Флаг для отслеживания – менялись ли данные
        private static Boolean isDataChanged;
        public UserControl2()
        {
            isDataChanged = false;
            InitializeComponent();
        }

        public Boolean getIsDataChanged()
        {
            return isDataChanged;
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}
