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
            switch (GridTab.SelectedIndex)
            {
                case 0: //Активная вкладка Диаметр – Шаг
                    DeleteRowFromDataTable(DiamStep, DiamStepView.SelectedIndex);
                    RecalcNum(DiamStep);
                    break;
                case 1: //Активная вкладка Диаметр – Цена
                    DeleteRowFromDataTable(DiamCost, DiamCostView.SelectedIndex);
                    RecalcNum(DiamCost);
                    break;
                case 2: //Активная вкладка Длина
                    DeleteRowFromDataTable(Length, LengthView.SelectedIndex);
                    RecalcNum(Length);
                    break;
            }
        }

        private void Button_Add_Click(object sender, RoutedEventArgs e)
        {
            switch (GridTab.SelectedIndex)
            {
                case 0: //Активная вкладка Диаметр – Шаг
                    DiamStep.Rows.Add();
                    RecalcNum(DiamStep);
                    break;
                case 1: //Активная вкладка Диаметр – Цена
                    DiamCost.Rows.Add();
                    RecalcNum(DiamCost);
                    break;
                case 2: //Активная вкладка Длина
                    Length.Rows.Add();
                    RecalcNum(Length);
                    break;
            }
        }

        //Вспомогательный метод для пересчета номеров строк таблиц в ui
        private void RecalcNum(DataTable dataTable)
        {
            int num = 1;
            foreach (DataRow row in dataTable.Rows)
            {
                row["Num"] = num;
                num ++;
            }
        }

        //Вспомогательный метод для удаления строки из таблицы
        private void DeleteRowFromDataTable(DataTable dataTable, int rowNum)
        {
            if (rowNum != -1)
            {
                dataTable.Rows.RemoveAt(rowNum);
            }
        }
        
        //Запрещаем ввод не чисел и нуля как первого элемента для таблицы Диаметр – Шаг
        private void DiamStep_PreviewInput(object sender, TextCompositionEventArgs e)                            
        {
            var textBox = e.OriginalSource as TextBox;
            string currentText = textBox?.Text;

            // Проверяем, является ли вводимый текст числом
            if (!Tools.IsInt(e.Text) || (e.Text == "0" && (currentText == null || currentText.Length == 0)))
            {
                e.Handled = true; // Запрещаем ввод
            }
        }

        //Запрещаем ввод не чисел и нуля как первого элемента для таблицы Диаметр – Цена
        private void DiamCost_PreviewInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = e.OriginalSource as TextBox;
            string currentText = textBox?.Text;

            // Проверяем, является ли вводимый текст числом
            if (!Tools.IsInt(e.Text) || (e.Text == "0" && (currentText == null || currentText.Length == 0)))
            {
                e.Handled = true; // Запрещаем ввод
            }
        }

        //Запрещаем ввод не чисел и нуля как первого элемента для таблицы Длина
        private void length_PreviewInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = e.OriginalSource as TextBox;
            string currentText = textBox?.Text;

            // Проверяем, является ли вводимый текст числом
            if (!Tools.IsInt(e.Text) || (e.Text == "0" && (currentText == null || currentText.Length == 0)))
            {
                e.Handled = true; // Запрещаем ввод
            }
        }
    }
}
