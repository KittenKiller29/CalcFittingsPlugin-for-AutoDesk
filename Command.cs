using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;

namespace CalcFittingsPlugin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        static AddInId AddInId = new AddInId(new Guid("030438B6-5743-4CD5-A0A4-061178C021B9"));

        public static UIDocument uiDoc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UserControl1 view = new UserControl1();
            uiDoc = commandData.Application.ActiveUIDocument;

            view.Show();
            return Result.Succeeded;
        }

        public static bool ValidateLevel(string lvlName, UIDocument uiDoc)
        {
            try
            {
                Document doc = uiDoc.Document;

                // 1. Получаем уровень по имени
                Level targetLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => l.Name.Equals(lvlName));

                if (targetLevel == null)
                {
                    MessageBox.Show($"Не удалось выполнить расчет, отсутствует уровень '{lvlName}'",
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 2. Проверяем существование плана этажа
                ViewPlan floorPlan = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate && v.GenLevel?.Id == targetLevel.Id);

                if (floorPlan == null)
                {
                    MessageBox.Show($"Для уровня '{lvlName}' не найден план этажа",
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 3. Ищем все плиты перекрытия на этом уровне
                List<Floor> floors = new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor))
                    .WhereElementIsNotElementType()
                    .Cast<Floor>()
                    .Where(f => f.LevelId == targetLevel.Id)
                    .Where(f => f.GetTypeId() != ElementId.InvalidElementId)
                    .ToList();

                // 4. Проверяем количество плит
                if (floors.Count == 0)
                {
                    MessageBox.Show($"На уровне '{lvlName}' не найдено плит перекрытия",
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                Floor validFloor = null;

                // 5. Если плита одна - она валидна
                if (floors.Count == 1)
                {
                    validFloor = floors[0];
                }
                // 6. Если плит несколько - выбираем по соответствию плану
                else
                {
                    validFloor = GetFloorMatchingPlan(floors, floorPlan, doc);
                    if (validFloor == null)
                    {
                        MessageBox.Show($"Не удалось определить основную плиту на уровне '{lvlName}'",
                                      "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }

                // 7. Активируем 3D вид и выделяем плиту
                View3D view3D = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate);

                if (view3D != null)
                {
                    uiDoc.ActiveView = view3D;
                    uiDoc.Selection.SetElementIds(new List<ElementId> { validFloor.Id });
                    uiDoc.ShowElements(new List<ElementId> { validFloor.Id });
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при проверке уровня: {ex.Message}",
                              "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private static Floor GetFloorMatchingPlan(List<Floor> floors, ViewPlan plan, Document doc)
        {
            // 1. Получаем все видимые элементы на плане
            ICollection<ElementId> visibleElements =
                new FilteredElementCollector(doc, plan.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds();

            // 2. Ищем плиту, которая есть на плане
            foreach (Floor floor in floors)
            {
                if (visibleElements.Contains(floor.Id))
                {
                    return floor;
                }
            }

            // 3. Если не нашли по Id, пытаемся найти по геометрии
            Outline planOutline = GetPlanExtents(plan);
            if (planOutline != null)
            {
                return floors.OrderByDescending(f => {
                    BoundingBoxXYZ bbox = f.get_BoundingBox(null);
                    if (bbox == null) return 0;
                    return GetOverlapArea(bbox, planOutline);
                }).FirstOrDefault();
            }

            return null;
        }

        private static Outline GetPlanExtents(ViewPlan plan)
        {
            try
            {
                Element planElement = plan as Element;
                Parameter minX = planElement.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                Parameter maxX = planElement.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_NEAR);

                if (minX != null && maxX != null)
                {
                    return new Outline(
                        new XYZ(minX.AsDouble(), 0, 0),
                        new XYZ(maxX.AsDouble(), 0, 0));
                }
            }
            catch { }
            return null;
        }

        private static double GetOverlapArea(BoundingBoxXYZ bbox, Outline outline)
        {
            double minX = Math.Max(bbox.Min.X, outline.MinimumPoint.X);
            double maxX = Math.Min(bbox.Max.X, outline.MaximumPoint.X);

            if (minX >= maxX) return 0;

            double minY = Math.Max(bbox.Min.Y, outline.MinimumPoint.Y);
            double maxY = Math.Min(bbox.Max.Y, outline.MaximumPoint.Y);

            if (minY >= maxY) return 0;

            return (maxX - minX) * (maxY - minY);
        }
    }
}
