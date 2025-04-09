using System;
using System.Data;
using System.Collections;
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

        public static bool ValidateLevel(string lvlName, UIDocument uiDoc, out List<Floor> _floors)
        {
            try
            {
                Document doc = uiDoc.Document;
                _floors = null;
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

                // 2. Ищем все плиты перекрытия на этом уровне
                List<Floor> floors = new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor))
                    .WhereElementIsNotElementType()
                    .Cast<Floor>()
                    .Where(f => f.LevelId == targetLevel.Id)
                    .Where(f => f.GetTypeId() != ElementId.InvalidElementId)
                    .ToList();

                _floors = floors;

                // 3. Проверяем количество плит
                if (floors.Count == 0)
                {
                    MessageBox.Show($"На уровне '{lvlName}' не найдено плит перекрытия",
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 4. Активируем 3D вид
                View3D view3D = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate);

                if (view3D != null)
                {
                    uiDoc.ActiveView = view3D;
                    uiDoc.Selection.SetElementIds(floors.Select(f => f.Id).ToList());
                    uiDoc.ShowElements(floors.Select(f => f.Id).ToList());
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при проверке уровня: {ex.Message}",
                              "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                _floors = null;
                return false;
            }
        }

        public static void GetNodeTable(Floor floor, DataTable allFit, out DataTable thisFit)
        {
            thisFit = allFit.Copy();
            thisFit.Clear();

            Document doc = floor.Document;

            // Получаем геометрию плиты
            Options geomOptions = new Options();
            geomOptions.ComputeReferences = true;
            GeometryElement geomElem = floor.get_Geometry(geomOptions);

            // Получаем верхнюю грань плиты
            PlanarFace topFace = null;
            double maxElevation = double.MinValue;

            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace planarFace)
                        {
                            XYZ normal = planarFace.FaceNormal;

                            // Проверяем, что это верхняя грань (нормаль направлена вверх)
                            if (normal.Z > 0.9) // Близко к вертикали вверх
                            {
                                double currentElevation = planarFace.Origin.Z;
                                if (currentElevation > maxElevation)
                                {
                                    maxElevation = currentElevation;
                                    topFace = planarFace;
                                }
                            }
                        }
                    }
                }
            }

            if (topFace == null)
            {
                return; // Не нашли верхнюю грань
            }

            // Получаем контуры плиты (внешний и внутренние - отверстия)
            IList<CurveLoop> boundaryLoops = topFace.GetEdgesAsCurveLoops();
            if (boundaryLoops.Count == 0)
            {
                return;
            }

            // Внешний контур плиты (первый в списке)
            CurveLoop outerBoundary = boundaryLoops[0];

            // Получаем уровень плиты и его высоту
            Level level = doc.GetElement(floor.LevelId) as Level;
            double floorElevation = level.Elevation;

            // Проверяем каждый узел на принадлежность плите
            foreach (DataRow row in allFit.Rows)
            {
                try
                {
                    // Получаем координаты узла
                    double x = Convert.ToDouble(row[Tools.HeadersTemplate[2]]);
                    double y = Convert.ToDouble(row[Tools.HeadersTemplate[3]]);
                    double z = Convert.ToDouble(row[Tools.HeadersTemplate[4]]);

                    // 1. Проверяем высоту (Z-координата должна быть близка к высоте плиты)
                    if (Math.Abs(z - floorElevation) > 0.1) // Допустимая погрешность 10 см
                    {
                        continue;
                    }

                    XYZ point = new XYZ(x, y, z);

                    // 2. Проверяем, находится ли точка внутри внешнего контура
                    if (!IsPointInsideCurveLoop(outerBoundary, point))
                    {
                        continue;
                    }

                    // 3. Проверяем, не попадает ли точка в отверстие
                    bool isInsideHole = false;
                    for (int i = 1; i < boundaryLoops.Count; i++)
                    {
                        if (IsPointInsideCurveLoop(boundaryLoops[i], point))
                        {
                            isInsideHole = true;
                            break;
                        }
                    }

                    if (!isInsideHole)
                    {
                        // Добавляем узел в таблицу
                        DataRow newRow = thisFit.NewRow();
                        newRow.ItemArray = row.ItemArray;
                        thisFit.Rows.Add(newRow);
                    }
                }
                catch
                {
                    // Пропускаем строки с ошибками
                    continue;
                }
            }
        }

        // Улучшенный метод для проверки нахождения точки внутри многоугольника
        private static bool IsPointInsideCurveLoop(CurveLoop loop, XYZ point)
        {
            // Преобразуем контур в список точек
            List<XYZ> polygonPoints = new List<XYZ>();
            foreach (Curve curve in loop)
            {
                polygonPoints.Add(curve.GetEndPoint(0));
            }

            // Алгоритм "ray casting" для проверки принадлежности точки многоугольнику
            int n = polygonPoints.Count;
            bool inside = false;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                XYZ pi = polygonPoints[i];
                XYZ pj = polygonPoints[j];

                // Проверяем пересечение луча с ребром многоугольника
                if (((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                    (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
