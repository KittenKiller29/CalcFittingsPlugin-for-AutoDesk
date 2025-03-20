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
        private object oldCellValue;
        //Флаг для отслеживания – менялись ли данные
        private static Boolean isDataChanged;
        public UserControl2()
        {
            DiamStep = new DataTable();
            DiamCost = new DataTable();
            Length = new DataTable();
            isDataChanged = false;
            InitializeComponent();

            //Следующий блок – подписываемся на различные однотипные события для DataGrid
            DiamStepView.BeginningEdit += DataGrid_BeginningEdit;
            DiamStepView.CellEditEnding += DataGrid_CellEditEnding;

            DiamCostView.BeginningEdit += DataGrid_BeginningEdit;
            DiamCostView.CellEditEnding += DataGrid_CellEditEnding;

            LengthView.BeginningEdit += DataGrid_BeginningEdit;
            LengthView.CellEditEnding += DataGrid_CellEditEnding;

            DiamStepView.LoadingRow += DataGrid_LoadingRow;
            DiamCostView.LoadingRow += DataGrid_LoadingRow;
            LengthView.LoadingRow += DataGrid_LoadingRow;

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

        //функция – валидатор перед сохранением таблиц в файл
        private string ValidateSave()
        {
            string message = "";

            return message;
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // При смене вкладки завершаем редактирование во всех DataGrid
            CommitAllDataGrids();
        }

        //Данный метод завершает редактирование во всех гридах, если завершить его не удалось – отменяет изменения
        private void CommitAllDataGrids()
        {
            if (!CommitDataGridEditing(DiamStepView))
            {
                DiamStepView.CancelEdit();
            }

            if (!CommitDataGridEditing(DiamCostView))
            {
                DiamCostView.CancelEdit();
            }

            if (!CommitDataGridEditing(LengthView))
            {
                LengthView.CancelEdit();
            }
        }

        //Данный метод отслеживает, удалось ли сохранить изменения в гриде
        private bool CommitDataGridEditing(DataGrid dataGrid)
        {
            try
            {
                bool commitSuccess = dataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                commitSuccess &= dataGrid.CommitEdit(DataGridEditingUnit.Row, true);

                return commitSuccess;
            }
            catch 
            {
                return false;
            }
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            string validateMsg = ValidateSave();
            if (validateMsg == "")
            {
                try
                {
                    isDataChanged = true;
                    DataFile.UpdateAllData(DiamStep, DiamCost, Length);
                    SaveButton.IsEnabled = false;
                    MessageBox.Show(Tools.ChangesSaved, "Сохранение", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch
                {
                    MessageBox.Show(Tools.ChangesNotSaved, "Сохранение", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(validateMsg, "Сохранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

            SaveButton.IsEnabled = true;
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

            SaveButton.IsEnabled = true;
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

        //Запрещаем ввод не чисел и нуля как первого элемента для таблицы Длина
        private void DataGrid_PreviewInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = e.OriginalSource as TextBox;
            string currentText = textBox?.Text;

            // Проверяем, является ли вводимый текст числом
            if (!Tools.IsInt(e.Text) || (e.Text == "0" && (currentText == null || currentText.Length == 0)) || (e.Text.Contains(" ")))
            {
                e.Handled = true; // Запрещаем ввод
            }
        }

        //Отслеживаем изменение значения в ячейке чтобы активировать сохранение
        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var editedText = (e.EditingElement as TextBox)?.Text;
            if (oldCellValue?.ToString() != editedText)
            {
                SaveButton.IsEnabled = true; 
            }
        }

        //Запоминаем начальное значение чтобы корректно активировать сохранение
        private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid != null && e.Row.Item is DataRowView row)
            {
                oldCellValue = row[e.Column.DisplayIndex]; 
            }
        }

        //Подписываемся на событие PreviewInput для всех TextBox в ячейках
        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.PreviewTextInput += DataGrid_PreviewInput;
        }
    }
}
