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
using Microsoft.VisualBasic.FileIO;
using Autodesk.Revit.DB;

namespace CalcFittingsPlugin
{
    /// <summary>
    /// Логика взаимодействия для UserControl3.xaml
    /// </summary>
    public partial class UserControl3 : Window
    {
        private string lastMessage;
        public UserControl3(string lvlName)
        {
            InitializeComponent();
            LvlTextBox.Text = lvlName;
            lastMessage = "";
        }

        private void Delete3DButton_Click(object sender, RoutedEventArgs e)
        {
            string msg = "";
            try
            {

                msg = ValidateLVL(false);

                if (msg != "") throw new Exception();

                //Прошли валидацию, удаляем армирование


                lastMessage = "На уровне '" + LvlTextBox.Text + "' удалено дополнительное армирование (3D модель)";
            }
            catch
            {
                if (msg == "")
                {
                    msg = "При удалении армирования с 3D модели плит уровня '" + LvlTextBox.Text + "'возникла ошибка, армирование не удалено.";
                }

                lastMessage = "Не удалось удалить дополнительное армирование на уровне '" + LvlTextBox.Text + "' (3D модель)";
            }
            finally
            {
                if(msg != "")
                {
                    MessageBox.Show(msg, "Ошибка удаления армирования", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                this.Tag = true;
                this.Close();
            }
        }

        private void Delete2DButton_Click(object sender, RoutedEventArgs e)
        {
            string msg = "";
            try
            {

                msg = ValidateLVL(true);

                if (msg != "") throw new Exception();

                //Прошли валидацию, запускаем удаление




                lastMessage = "На уровне '" + LvlTextBox.Text + "' удалено дополнительное армирование (2D план)";
            }
            catch
            {
                if (msg == "")
                {
                    msg = "При удалении армирования с 2D плана уровня '" + LvlTextBox.Text + "'возникла ошибка, армирование не удалено.";
                }

                lastMessage = "Не удалось удалить дополнительное армирование на уровне '" + LvlTextBox.Text + "' (2D план)";
            }
            finally
            {
                if (msg != "")
                {
                    MessageBox.Show(msg, "Ошибка удаления армирования", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                this.Tag = true;
                this.Close();
            }
        }

        private string ValidateLVL(bool plan)
        {
            string msg = "";
            int count = 0;
            //Валидация наличия самого уровня
            Level targetLevel = new FilteredElementCollector(Command.uiDoc.Document)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => l.Name.Equals(LvlTextBox.Text));

            if(targetLevel == null)
            {
                return "Невозможно удалить дополнительное армирование, отсутствует уровень '"+ LvlTextBox.Text + "'";
            }

            //Валидация для 2D плана
            if (plan)
            {
                //Проверяем, существует ли план для этажа

                var floorPlans = new FilteredElementCollector(Command.uiDoc.Document)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(vp => vp.ViewType == ViewType.FloorPlan);

                // Проверяем наличие вида для данного уровня
                foreach (ViewPlan plan2d in floorPlans)
                {
                    if (plan2d.GenLevel != null && plan2d.GenLevel.Id == targetLevel.Id)
                    {
                        count += 1;
                    }
                }

                if(count == 0)
                {
                    return "Отсутствует план уровня '" + LvlTextBox.Text + "'";
                }
                else if(count > 1)
                {
                    return "Найдено несколько планов для уровня '" + LvlTextBox.Text + "', удаление возможно только при существовании одного плана уровня";
                }
            }


            //Валидация для 3D модели
            if(!plan)
            {
                List<Floor> floors = new FilteredElementCollector(Command.uiDoc.Document)
                    .OfClass(typeof(Floor))
                    .WhereElementIsNotElementType()
                    .Cast<Floor>()
                    .Where(f => f.LevelId == targetLevel.Id)
                    .Where(f => f.GetTypeId() != ElementId.InvalidElementId)
                    .ToList();
                if(floors.Count == 0)
                {
                    return "На уровне '" + LvlTextBox.Text + "' отсутствуют плиты перекрытия";
                }
            }

            return msg;
        }

        public string GetLastMessage()
        {
            return lastMessage;
        }
    }
}
