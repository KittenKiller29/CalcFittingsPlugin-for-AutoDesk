using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
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
        private static DataTable DiamStep;
        private static DataTable DiamCost;
        private static DataTable Length;
        //Флаг для отслеживания – менялись ли данные
        private static Boolean isDataChanged;
        public UserControl2()
        {
            DiamStep = new DataTable();
            DiamCost = new DataTable();
            Length = new DataTable();
            isDataChanged = false;
            InitializeComponent();
            CreateDataProviders();
            DataFile.LoadAllData(DiamStep, DiamCost, Length);
        }

        //Метод для создания таблиц и установления их в качестве провайдера данных 
        private void CreateDataProviders()
        {
            //Заполняем Диаметр – Шаг
            DiamStep.Columns.Add("Num", typeof(int));
            DiamStep.Columns.Add("Diam", typeof(int));
            DiamStep.Columns.Add("Step", typeof(int));
            DiamStepView.ItemsSource = DiamStep.DefaultView;

            //Заполняем Диаметр – Цена
            DiamCost.Columns.Add("Num", typeof(int));
            DiamCost.Columns.Add("Diam", typeof(int));
            DiamCost.Columns.Add("Cost", typeof(int));
            DiamCostView.ItemsSource = DiamCost.DefaultView;

            //Заполняем Длина
            Length.Columns.Add("Num", typeof(int));
            Length.Columns.Add("Length", typeof(int));
            LengthView.ItemsSource = Length.DefaultView;
        }

        public Boolean getIsDataChanged()
        {
            return isDataChanged;
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            isDataChanged = true;
            DataFile.UpdateAllData();
        }

        private void Button_Delete_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Button_Add_Click(object sender, RoutedEventArgs e)
        {
            switch (GridTab.SelectedIndex)
            {
                case 0: //Активная вкладка Диаметр – Шаг
                    DiamStep.Rows.Add();
                    break;
                case 1: //Активная вкладка Диаметр – Цена
                    DiamCost.Rows.Add();
                    break;
                case 2: //Активная вкладка Длина
                    Length.Rows.Add();
                    break;

            }  
        }
    }
}
