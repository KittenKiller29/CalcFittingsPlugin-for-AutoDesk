using System;
using System.Data;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Diagnostics;
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

        public static VisualizationHandler VisualizationHandler { get; set; }
        public static ExternalEvent VisualizationEvent { get; set; }
        public static CleanHandler CleanHandler { get; set; }
        public static ExternalEvent CleanEvent { get; set; }
        public static PlanarVisualizationHandler PlanarVisualizationHandler { get; set; }
        public static ExternalEvent PlanarVisualizationEvent { get; set; }
        public static PlanarCleanHandler PlanarCleanHandler { get; set; }
        public static ExternalEvent PlanarCleanEvent { get; set; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UserControl1 view = new UserControl1();
            uiDoc = commandData.Application.ActiveUIDocument;
            CleanHandler = new CleanHandler();
            CleanEvent = ExternalEvent.Create(CleanHandler);

            VisualizationHandler = new VisualizationHandler();
            VisualizationEvent = ExternalEvent.Create(VisualizationHandler);

            PlanarVisualizationHandler = new PlanarVisualizationHandler();
            PlanarVisualizationEvent = ExternalEvent.Create(PlanarVisualizationHandler);

            PlanarCleanHandler = new PlanarCleanHandler();
            PlanarCleanEvent = ExternalEvent.Create(PlanarCleanHandler);

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

    [Transaction(TransactionMode.Manual)]
    public class CleanHandler : IExternalEventHandler
    {
        public List<Floor> Floors { get; set; }
        public static UIDocument UiDoc { get; set; }
        public void Execute(UIApplication app)
        {
            UiDoc = app.ActiveUIDocument;
            var doc = UiDoc.Document;

            try
            {
                // Получаем ID целевых плит
                var targetFloorIds = Floors.Select(f => f.Id.IntegerValue).ToList();

                // Собираем все элементы визуализации
                var allVizElements = new FilteredElementCollector(doc)
                    .OfClass(typeof(DirectShape))
                    .Where(e => e.Name.StartsWith("Zone Boundary_") || e.Name.StartsWith("Rebar Visualization_"))
                    .ToList();

                // Фильтруем элементы, связанные с целевыми плитами
                var toDelete = new List<ElementId>();

                foreach (var element in allVizElements)
                {
                    // Получаем ID плиты из имени элемента
                    var nameParts = element.Name.Split('_');
                    if (nameParts.Length < 2) continue;

                    if (int.TryParse(nameParts.Last(), out int floorIdValue))
                    {
                        if (targetFloorIds.Contains(floorIdValue))
                        {
                            toDelete.Add(element.Id);
                        }
                    }
                }

                if (toDelete.Count > 0)
                {
                    using (Transaction t = new Transaction(doc, "Удаление предыдущей визуализации"))
                    {
                        t.Start();
                        doc.Delete(toDelete);
                        t.Commit();
                    }
                }

                View3D view3D = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate);

                UiDoc.ActiveView = view3D;
                UiDoc.Selection.SetElementIds(Floors.Select(f => f.Id).ToList());
                UiDoc.ShowElements(Floors.Select(f => f.Id).ToList());
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка очистки", ex.Message);
            }
        }
        public string GetName() => "Reinforcement Clean";
    }

    [Transaction(TransactionMode.Manual)]
    public class VisualizationHandler : IExternalEventHandler
    {
        public ReinforcementSolution Solution { get; set; }
        public int SolutionIndex { get; set; }
        public static UIDocument UiDoc { get; set; }
        public List<Floor> Floors { get; set; }

        public void Execute(UIApplication app)
        {
            var uiDoc = app.ActiveUIDocument;
            var doc = uiDoc.Document;
            UiDoc = uiDoc;

            View3D view3D = GetOrCreate3DView(doc);
            uiDoc.ActiveView = view3D;

            uiDoc.Selection.SetElementIds(Floors.Select(f => f.Id).ToList());
            uiDoc.ShowElements(Floors.Select(f => f.Id).ToList());

            try
            {
                // Если нет активной транзакции, используем обычную Transaction
                if (!doc.IsModifiable)
                {
                    using (var t = new Transaction(doc, "3D Visualization"))
                    {
                        t.Start();
                        ExecuteVisualization(doc, uiDoc, view3D);
                        t.Commit();
                    }
                }
                else // Если уже есть активная транзакция, используем SubTransaction
                {
                    using (var st = new SubTransaction(doc))
                    {
                        st.Start();
                        ExecuteVisualization(doc, uiDoc, view3D);
                        st.Commit();
                    }
                }

                uiDoc.RefreshActiveView();

            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", ex.ToString());
            }
        }

        private void ExecuteVisualization(Document doc, UIDocument uiDoc, View3D view3D)
        {
            CleanPreviousVisualization(doc, Floors);


            foreach (var zone in Solution.Zones)
            {
                VisualizeZone(doc, view3D, zone, Solution.Zones.IndexOf(zone) + 1);
            }
        }

        private void VisualizeZone(Document doc, View3D view, ZoneSolution zone, int zoneNumber)
        {
            if (zone.Nodes == null || zone.Nodes.Count == 0) return;

            // Получаем уровень плиты
            Floor floor = Floors[zone.Nodes[0].SlabId];
            PlanarFace topFace = GetTopFaceOfFloor(floor);
            double elevation = topFace.Origin.Z;

            // Границы зоны
            CreateZoneBoundary(doc, zone, elevation, floor.Id);

            // Арматура
            CreateRebarVisualization(doc, zone, elevation, floor.Id);

            // Текстовая аннотация
            //CreateZoneAnnotation(doc, view, zone, elevation, zoneNumber);
        }

        private PlanarFace GetTopFaceOfFloor(Floor floor)
        {
            Options geomOptions = new Options();
            geomOptions.ComputeReferences = true;
            GeometryElement geomElem = floor.get_Geometry(geomOptions);

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
                            if (normal.Z > 0.9) // Верхняя грань
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
            return topFace;
        }

        private void CreateZoneBoundary(Document doc, ZoneSolution zone, double elevation, ElementId floorId)
        {
            try
            {
                // Создаем список геометрических объектов (а не просто кривых)
                List<GeometryObject> geometryObjects = new List<GeometryObject>();

                double xMin = zone.Boundary.X;
                double yMin = zone.Boundary.Y;
                double xMax = xMin + zone.Boundary.Width;
                double yMax = yMin + zone.Boundary.Height;

                // Создаем кривые границы с визуальным смещением наружу
                double lineOffset = 0.05;

                // Добавляем кривые границы зоны
                geometryObjects.Add(Line.CreateBound(
                    new XYZ(xMin - lineOffset, yMin - lineOffset, elevation),
                    new XYZ(xMax + lineOffset, yMin - lineOffset, elevation)));

                geometryObjects.Add(Line.CreateBound(
                    new XYZ(xMax + lineOffset, yMin - lineOffset, elevation),
                    new XYZ(xMax + lineOffset, yMax + lineOffset, elevation)));

                geometryObjects.Add(Line.CreateBound(
                    new XYZ(xMax + lineOffset, yMax + lineOffset, elevation),
                    new XYZ(xMin - lineOffset, yMax + lineOffset, elevation)));

                geometryObjects.Add(Line.CreateBound(
                    new XYZ(xMin - lineOffset, yMax + lineOffset, elevation),
                    new XYZ(xMin - lineOffset, yMin - lineOffset, elevation)));

                // Создаем DirectShape
                DirectShape boundaryShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                boundaryShape.SetShape(geometryObjects); // Теперь передаем правильный тип
                boundaryShape.Name = $"Zone Boundary_{floorId}";

                SetFloorIdParameter(boundaryShape, floorId);

                // Настраиваем графическое отображение
                OverrideGraphicSettings ogs = new OverrideGraphicSettings()
                    .SetProjectionLineColor(new Color(0, 255, 0)) // Зеленый цвет
                    .SetSurfaceTransparency(0)
                    .SetProjectionLineWeight(1); // Толщина линии

                doc.ActiveView.SetElementOverrides(boundaryShape.Id, ogs);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка создания границы", ex.Message);
            }
        }

        private void CreateRebarVisualization(Document doc, ZoneSolution zone, double elevation, ElementId floorId)
        {
            try
            {
                double spacing = zone.Spacing / 1000.0;
                List<GeometryObject> geomObjs = new List<GeometryObject>();
                double offset = ((zone.Boundary.Height / spacing) - Math.Truncate(zone.Boundary.Height / spacing)) * spacing / 2;

                // Вертикальные стержни
                for (double y = zone.Boundary.Y; y <= zone.Boundary.Y + zone.Boundary.Height; y += spacing)
                {
                    geomObjs.Add(Line.CreateBound(
                        new XYZ(zone.Boundary.X, y + offset, elevation),
                        new XYZ(zone.Boundary.X + zone.Boundary.Width, y + offset, elevation)));
                }

                offset = ((zone.Boundary.Width / spacing) - Math.Truncate(zone.Boundary.Width / spacing)) * spacing / 2;

                // Горизонтальные стержни
                for (double x = zone.Boundary.X; x <= zone.Boundary.X + zone.Boundary.Width; x += spacing)
                {
                    geomObjs.Add(Line.CreateBound(
                        new XYZ(x + offset, zone.Boundary.Y, elevation),
                        new XYZ(x + offset, zone.Boundary.Y + zone.Boundary.Height, elevation)));
                }

                DirectShape rebarShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                rebarShape.SetShape(geomObjs);
                rebarShape.Name = $"Rebar Visualization_{floorId}";

                SetFloorIdParameter(rebarShape, floorId);

                OverrideGraphicSettings ogs = new OverrideGraphicSettings()
                    .SetProjectionLineColor(new Color(255, 0, 0))
                    .SetSurfaceTransparency(0)
                    .SetProjectionLineWeight(1);

                doc.ActiveView.SetElementOverrides(rebarShape.Id, ogs);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка создания арматуры", ex.Message);
            }
        }

        private void SetFloorIdParameter(Element element, ElementId floorId)
        {
            try
            {
                // Используем User-параметр для хранения ID плиты
                element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(floorId.ToString());
            }
            catch
            {
                // Альтернативный вариант, если параметр недоступен
                element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(floorId.ToString());
            }
        }


        public void CleanPreviousVisualization(Document doc, List<Floor> targetFloors)
        {
            try
            {
                // Получаем ID целевых плит
                var targetFloorIds = targetFloors.Select(f => f.Id.IntegerValue).ToList();

                // Собираем все элементы визуализации
                var allVizElements = new FilteredElementCollector(doc)
                    .OfClass(typeof(DirectShape))
                    .Where(e => e.Name.StartsWith("Zone Boundary_") || e.Name.StartsWith("Rebar Visualization_"))
                    .ToList();

                // Фильтруем элементы, связанные с целевыми плитами
                var toDelete = new List<ElementId>();

                foreach (var element in allVizElements)
                {
                    // Получаем ID плиты из имени элемента
                    var nameParts = element.Name.Split('_');
                    if (nameParts.Length < 2) continue;

                    if (int.TryParse(nameParts.Last(), out int floorIdValue))
                    {
                        if (targetFloorIds.Contains(floorIdValue))
                        {
                            toDelete.Add(element.Id);
                        }
                    }
                }

                if (toDelete.Count > 0)
                {
                    doc.Delete(toDelete);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка очистки", ex.Message);
            }
        }

        private View3D GetOrCreate3DView(Document doc)
        {
            // Ищем существующий вид без создания транзакции
            View3D view = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate);

            if (view == null)
            {
                MessageBox.Show("Не удалось визуализирвоать, не найдена модель", "Визуализация зон", MessageBoxButton.OK, MessageBoxImage.Warning);
                throw new Exception();
            }

            if (view != null)
            {
                Category modelLinesCategory = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
                view.SetCategoryHidden(modelLinesCategory.Id, false);
            }

            return view;
        }

        public string GetName() => "Reinforcement Visualization";
    }

    public class RebarConfig
    {
        public double Diameter { get; set; } // Диаметр арматуры (мм)
        public List<double> AvailableSpacings { get; set; } // Доступные шаги арматуры (мм)
        public double PricePerMeter { get; set; } // Цена за метр арматуры
    }

    public class Opening
    {
        public List<XYZ> Polygon { get; set; } // Многоугольник отверстия
        public int SlabId { get; set; } // ID плиты, к которой относится отверстие
        public Rectangle Boundary { get; set; } // Ограничивающий прямоугольник
    }

    public class Node
    {
        public string Type { get; set; }
        public int Number { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double ZCenter { get; set; }
        public double ZMin { get; set; }
        public double As1X { get; set; } // Требуемое армирование (см²/м)
        public double As2X { get; set; }
        public double As3Y { get; set; }
        public double As4Y { get; set; }
        public int SlabId { get; set; }
        public Rectangle Boundary { get; set; }
        public int ClusterVersion { get; set; }
        public double MedianLoad { get; set; }

        public void UpdateBoundary(Rectangle newBoundary)
        {
            this.Boundary = newBoundary;
            this.ClusterVersion++;
        }
    }

    public class SlabBoundaryChecker
    {
        /// <summary>
        /// Проверяет, что прямоугольная зона полностью находится внутри полигона плиты
        /// </summary>
        /// <param name="zoneBoundary">Границы зоны армирования</param>
        /// <param name="slabPolygon">Границы плиты (список точек по часовой стрелке)</param>
        /// <returns>True если зона полностью внутри плиты</returns>
        public static bool IsZoneInsideSlab(Rectangle zoneBoundary, List<XYZ> slabPolygon)
        {
            // 1. Получаем углы зоны
            var zoneCorners = GetRectangleCorners(zoneBoundary);

            // 2. Проверяем все углы зоны внутри полигона
            foreach (var corner in zoneCorners)
            {
                if (!IsPointInsidePolygon(corner, slabPolygon))
                    return false;
            }

            // 3. Проверяем, что ни одна из сторон зоны не пересекает границу плиты
            var zoneEdges = GetRectangleEdges(zoneBoundary);
            var slabEdges = GetPolygonEdges(slabPolygon);

            foreach (var zoneEdge in zoneEdges)
            {
                foreach (var slabEdge in slabEdges)
                {
                    if (LinesIntersect(zoneEdge.Start, zoneEdge.End,
                                     slabEdge.Start, slabEdge.End))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Проверяет, находится ли точка внутри полигона (алгоритм ray casting)
        /// </summary>
        public static bool IsPointInsidePolygon(XYZ point, List<XYZ> polygon)
        {
            int count = polygon.Count;
            bool inside = false;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) /
                              (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// Проверяет пересечение двух отрезков
        /// </summary>
        private static bool LinesIntersect(XYZ a1, XYZ a2, XYZ b1, XYZ b2)
        {
            double ccw1 = Cross(a2 - a1, b1 - a1);
            double ccw2 = Cross(a2 - a1, b2 - a1);
            if (ccw1 * ccw2 >= 0) return false;

            double ccw3 = Cross(b2 - b1, a1 - b1);
            double ccw4 = Cross(b2 - b1, a2 - b1);
            return ccw3 * ccw4 < 0;
        }

        private static double Cross(XYZ a, XYZ b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        /// <summary>
        /// Получает углы прямоугольника
        /// </summary>
        private static List<XYZ> GetRectangleCorners(Rectangle rect)
        {
            return new List<XYZ>
        {
            new XYZ(rect.X, rect.Y, 0),
            new XYZ(rect.X + rect.Width, rect.Y, 0),
            new XYZ(rect.X + rect.Width, rect.Y + rect.Height, 0),
            new XYZ(rect.X, rect.Y + rect.Height, 0)
        };
        }

        /// <summary>
        /// Получает стороны прямоугольника
        /// </summary>
        private static List<Edge> GetRectangleEdges(Rectangle rect)
        {
            var corners = GetRectangleCorners(rect);
            return new List<Edge>
        {
            new Edge(corners[0], corners[1]),
            new Edge(corners[1], corners[2]),
            new Edge(corners[2], corners[3]),
            new Edge(corners[3], corners[0])
        };
        }

        /// <summary>
        /// Получает стороны полигона
        /// </summary>
        private static List<Edge> GetPolygonEdges(List<XYZ> polygon)
        {
            var edges = new List<Edge>();
            for (int i = 0; i < polygon.Count; i++)
            {
                int j = (i + 1) % polygon.Count;
                edges.Add(new Edge(polygon[i], polygon[j]));
            }
            return edges;
        }

        /// <summary>
        /// Вычисляет площадь полигона (по формуле шнурков)
        /// </summary>
        public static double GetPolygonArea(List<XYZ> polygon)
        {
            double area = 0;
            int count = polygon.Count;

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                area += polygon[i].X * polygon[j].Y;
                area -= polygon[j].X * polygon[i].Y;
            }

            return Math.Abs(area / 2);
        }

        /// <summary>
        /// Получает границы плиты из Revit элемента
        /// </summary>
        public static List<XYZ> GetSlabBoundary(Floor slab)
        {
            var boundary = new List<XYZ>();

            Options geomOptions = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            using (GeometryElement geomElem = slab.get_Geometry(geomOptions))
            {
                foreach (GeometryObject geomObj in geomElem)
                {
                    if (geomObj is Solid solid)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            if (face is PlanarFace planarFace)
                            {
                                var curveLoop = planarFace.GetEdgesAsCurveLoops().FirstOrDefault();
                                if (curveLoop != null)
                                {
                                    boundary = curveLoop.Select(c => c.GetEndPoint(0)).ToList();
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return boundary;
        }

        /// <summary>
        /// Вспомогательный класс для хранения ребер
        /// </summary>
        private class Edge
        {
            public XYZ Start { get; }
            public XYZ End { get; }

            public Edge(XYZ start, XYZ end)
            {
                Start = start;
                End = end;
            }
        }
    }

    public class Split
    {
        public Line line { get; set; }
        public bool isVertical { get; set; }
    }

    public class ReinforcementOptimizer
    {
        // Конфигурация алгоритма
        public List<RebarConfig> AvailableRebars { get; set; }
        public List<double> StandardLengths { get; set; }
        public double[] BasicReinforcement { get; set; }
        public List<Opening> Openings { get; set; } = new List<Opening>();
        public int PopulationSize { get; set; } = 35;
        public int Generations { get; set; } = 400;
        public double MutationRate { get; set; } = 1;
        public int EliteCount { get; set; } = 7;
        public double MinRebarPerDirection { get; set; } = 2;
        public int NumOfSol { get; set; } = 5;
        private int defaultZonesCount;

        private List<List<XYZ>> poligonList;
        private static readonly Random Random = new Random();
        private List<Floor> _floors;
        private SpatialGrid<Node> _spatialGrid;
        private List<Split> lineList;
        private double FitnesCoef;

        public List<ReinforcementSolution> FindBestSolutions(List<List<Node>> slabsNodes, int solutionCount, List<Floor> floors)
        {
            _floors = floors;
            Openings = GetOpeningsFromRevit(floors);
            lineList = new List<Split>();

            // Создаем пространственную сетку для быстрого поиска узлов
            var allNodes = slabsNodes.SelectMany(x => x).ToList();
            _spatialGrid = new SpatialGrid<Node>(allNodes, 2.0);
            poligonList = new List<List<XYZ>>();

            FitnesCoef = 7000; 

            // Инициализация популяции с гарантированным покрытием
            var population = InitializePopulationWithCoverage(slabsNodes);

            for (int i = 0; i < _floors.Count; i++)
            {
                poligonList.Add(SlabBoundaryChecker.GetSlabBoundary(_floors[i]));
                Split split = new Split();
                (split.line, split.isVertical) = GetOptimalSplitLine(poligonList[i]);
                lineList.Add(split);
            }

            MergeAllOverlappingZones(population[0]);

            //Корректируем первое решение, потом просто создаем много его копий 
            CorrectZones(population);

            defaultZonesCount = population[0].Zones.Count;

            Generations = (int)Math.Truncate(defaultZonesCount * 1.4);
            PopulationSize -= (int)Math.Truncate((double)(Generations / 100));

            //Заполянем список популяции
            for (int i = 1; i < PopulationSize; i++)
                population.Add(population[0].ShallowCopy());


            // Эволюционный процесс с проверкой покрытия
            for (int gen = 0; gen < Generations; gen++)
            { 
                population = EvolvePopulation(population, slabsNodes);
            }


            // Возврат лучших уникальных решений с минимальной ценой
            return GetBestUniqueSolutions(population, solutionCount);
        }

        //Данным методом корректируем начальное разбиение, чтобы зоны не выходили за пределы плиты
        private void CorrectZones(List<ReinforcementSolution> population)
        {
            for(int i = 0; i < population[0].Zones.Count; i++)
            {
                List<XYZ> poligon = poligonList[population[0].Zones[i].ZoneID];
                if (!SlabBoundaryChecker.IsZoneInsideSlab(population[0].Zones[i].Boundary, poligon))
                {

                    for (int j = 0; j < 4; j++)
                    {
                        bool flag = false;
                        ZoneSolution correctZone = new ZoneSolution();
                        correctZone.CopyFrom(population[0].Zones[i]);

                        for (int k = 0; k < 5; k++)
                        {
                            if (j == 0)
                                correctZone.Boundary.X += 0.2;
                            if (j == 1)
                                correctZone.Boundary.X -= 0.2;
                            if (j == 2)
                                correctZone.Boundary.Y += 0.2;
                            if (j == 3)
                                correctZone.Boundary.Y -= 0.2;

                            if (SlabBoundaryChecker.IsZoneInsideSlab(correctZone.Boundary, poligon) &&
                                correctZone.Boundary.Contains(correctZone.Nodes[0].X, correctZone.Nodes[0].Y))
                            {
                                population[0].Zones[i] = correctZone;
                                flag = true;
                                break;
                            }
                        }

                        if (flag == true)
                            break;
                    }

                }
            }
        }

        private List<ReinforcementSolution> InitializePopulationWithCoverage(List<List<Node>> slabsNodes)
        {
            var population = new List<ReinforcementSolution>();
            var allNodes = slabsNodes.SelectMany(x => x).ToList();
            
            population.Add(CreateMinimalCoverageSolution(slabsNodes));
            //Все решения одинаковые, считаем их плохо приспособенными
            population[0].FitnesCost = 1000000 * 1e7;
            

            return population;
        }



        public List<ReinforcementSolution> EvolvePopulation(List<ReinforcementSolution> population, List<List<Node>> slabsNodes)
        {
            var evolvedPopulation = new List<ReinforcementSolution>();

            var elites = population.OrderBy(s => s.FitnesCost).ThenBy(s => -s.Zones.Average(z => z.Nodes.Count)).Take(EliteCount).ToList();
            evolvedPopulation.AddRange(elites.Select(e => e.ShallowCopy()));

            var tmpPop1 = new List<ReinforcementSolution>();

            for (int i = EliteCount; i < PopulationSize; i++)
            {
                tmpPop1.Add(new ReinforcementSolution());
            }

            //Кроссовер
            //Выбираем среди лучших решений два случаных, объединяем их зоны по разбиению плиты
            Parallel.For(0, tmpPop1.Count, i =>
            {
                var rand = new Random();
                int idx1 = rand.Next(elites.Count);
                int idx2;

                // Гарантируем, что выбираем разные решения
                do
                {
                    idx2 = rand.Next(elites.Count);
                } while (idx2 == idx1);

                tmpPop1[i] = CombineEliteZones(elites[idx1], elites[idx2]).ShallowCopy();

                MergeAllOverlappingZones(tmpPop1[i]);
                tmpPop1[i].UpdateTotalCost();
            });



            // Мутация
            Parallel.For(0, tmpPop1.Count, i =>
            {
                Mutate(tmpPop1[i]);
                MergeAllOverlappingZones(tmpPop1[i]);
                tmpPop1[i].UpdateTotalCost();
            });


            for (int i = EliteCount; i < PopulationSize; i++)
                evolvedPopulation.Add(tmpPop1[i - EliteCount]);


            for (int i = EliteCount; i < PopulationSize; i++)
            {
                evolvedPopulation[i].FitnesCost = evolvedPopulation[i].TotalCost;
            }

            var tmpPop = new List<ReinforcementSolution>();

            for (int i = EliteCount; i < PopulationSize; i++)
            {
                tmpPop.Add(evolvedPopulation[i]);
            }

            //Запускаем параллельную обработку зон и отверстий для оценки приспособленности
            Parallel.For(0, tmpPop.Count, i =>
            {


                /*for (int j = 0; j < solution.Zones.Count; j++)
                {
                    List<XYZ> poligon = poligonList[solution.Zones[j].ZoneID];

                    if (!SlabBoundaryChecker.IsZoneInsideSlab(solution.Zones[j].Boundary, poligon))
                    {
                        solution.FitnesCost += 10 * 1e7;
                        break;
                    }
                }*/

                for (int j = 0; j < tmpPop[i].Zones.Count; j++)
                {
                    for (int k = 0; k < Openings.Count; k++)
                    {

                        double overlapArea = CalculateOverlapArea(tmpPop[i].Zones[j], Openings[k]);
                        if (overlapArea <= 0) continue;

                        double openingArea = Openings[k].Boundary.Width * Openings[k].Boundary.Height;
                        double overlapRatio = overlapArea / openingArea;

                        if (overlapRatio > 0.1)
                        {
                            tmpPop[i].FitnesCost +=  1e7 * overlapRatio * 10;
                        }

                    }

                    //Дополнительно штрафуем за зоны с 1 узлом
                    //int singleNodeZones = tmpPop[i].Zones.Count(z => z.Nodes.Count <= 1);
                    //tmpPop[i].FitnesCost += Math.Pow(2, singleNodeZones) * 1e5;
                }

                //Штрафуем зоны за каждое перекрытие

                for (int j = 0; j < tmpPop[i].Zones.Count; j++)
                {
                    for (int k = 0; k < tmpPop[i].Zones.Count; k++)
                    {
                        if (j != k &&
                            tmpPop[i].Zones[j].Boundary.Intersects(tmpPop[i].Zones[k].Boundary))
                        {
                            tmpPop[i].FitnesCost += 1e7;
                        }

                    }

                    //Дополнительный штраф за зоны одиночки
                    if (tmpPop[i].Zones[j].Nodes.Count <= 1)
                        tmpPop[i].FitnesCost += 10000;

                }
                    tmpPop[i].FitnesCost += tmpPop[i].Zones.Count * FitnesCoef;
                
            });

            for (int i = EliteCount; i < PopulationSize; i++)
                evolvedPopulation[i] = tmpPop[i - EliteCount];

            //Обновляем стоимость всех решений популяции
            Parallel.For(0, evolvedPopulation.Count, i =>
            {
                evolvedPopulation[i].UpdateTotalCost();
                if (evolvedPopulation[i].FitnesCost == defaultZonesCount)
                    evolvedPopulation[i].FitnesCost += 1e7 * 100;
            });

            return evolvedPopulation;
        }

        private ReinforcementSolution CombineEliteZones(ReinforcementSolution elite1, ReinforcementSolution elite2)
        {
            var child = new ReinforcementSolution();

            

            for (int i = 0; i < elite1.Zones.Count; i++)
            {
                var zone = new ZoneSolution();
                zone.CopyFrom(elite1.Zones[i]);
                if (!lineList[elite1.Zones[i].ZoneID].isVertical)
                {
                    if (elite1.Zones[i].Boundary.X > lineList[elite1.Zones[i].ZoneID].line.Origin.X)
                        child.Zones.Add(zone);
                }
                else
                {
                    if (elite1.Zones[i].Boundary.Y > lineList[elite1.Zones[i].ZoneID].line.Origin.Y)
                        child.Zones.Add(zone);
                }

            }

            for (int i = 0; i < elite2.Zones.Count; i++)
            {
                var zone = new ZoneSolution();
                zone.CopyFrom(elite2.Zones[i]);
                if (!lineList[elite2.Zones[i].ZoneID].isVertical)
                {
                    if (elite2.Zones[i].Boundary.X < lineList[elite2.Zones[i].ZoneID].line.Origin.X)
                        child.Zones.Add(zone);
                    
                }
                else
                {
                    if (elite2.Zones[i].Boundary.Y < lineList[elite2.Zones[i].ZoneID].line.Origin.Y)
                        child.Zones.Add(zone);
                }

            }

            child.UpdateTotalCost();

            return child;
        }

        private (Line splitLine, bool isVertical) GetOptimalSplitLine(List<XYZ> polygon)
        {
            // Вычисляем bounding box полигона
            var minX = polygon.Min(p => p.X);
            var maxX = polygon.Max(p => p.X);
            var minY = polygon.Min(p => p.Y);
            var maxY = polygon.Max(p => p.Y);

            // Разделяем по большей стороне
            bool isVertical = (maxX - minX) > (maxY - minY);

            // Центр плиты
            var centerX = (minX + maxX) / 2;
            var centerY = (minY + maxY) / 2;

            return isVertical
                ? (Line.CreateBound(new XYZ(centerX, minY, 0), new XYZ(centerX, maxY, 0)), true)
                : (Line.CreateBound(new XYZ(minX, centerY, 0), new XYZ(maxX, centerY, 0)), false);
        }

        private double CalculateOverlapArea(ZoneSolution zone, Opening opening)
        {
            if (!zone.Boundary.Intersects(opening.Boundary))
                return 0;

            // Вычисляем пересечение прямоугольников
            double x1 = Math.Max(zone.Boundary.X, opening.Boundary.X);
            double y1 = Math.Max(zone.Boundary.Y, opening.Boundary.Y);
            double x2 = Math.Min(zone.Boundary.X + zone.Boundary.Width, opening.Boundary.X + opening.Boundary.Width);
            double y2 = Math.Min(zone.Boundary.Y + zone.Boundary.Height, opening.Boundary.Y + opening.Boundary.Height);

            double overlapWidth = x2 - x1;
            double overlapHeight = y2 - y1;

            if (overlapWidth <= 0 || overlapHeight <= 0)
                return 0;

            return overlapWidth * overlapHeight;
        }

        public void Mutate(ReinforcementSolution solution)
        {
            if (Random.NextDouble() > MutationRate) return;

            // 1. Объединение двух случайных зон (90% вероятности)
            if (Random.NextDouble() < 0.9 && solution.Zones.Count > 1)
            {
                int idx1 = Random.Next(solution.Zones.Count);
                int idx2 = Random.Next(solution.Zones.Count);
                //Даем 15 попыток найти две зоны, у которых расстояние меньше 2 метров
                for (int i = 0; i < 25; i++)
                {
                    idx1 = Random.Next(solution.Zones.Count);
                    idx2 = Random.Next(solution.Zones.Count);
                    double distanse = ZoneSolution.CalculateDistanceBetweenZones(solution.Zones[idx1], solution.Zones[idx2]);
                    if ((distanse <= 3) &&
                        (solution.Zones[idx1].Boundary.Height + solution.Zones[idx2].Boundary.Height <= StandardLengths.Max()) &&
                        (solution.Zones[idx1].Boundary.Width + solution.Zones[idx2].Boundary.Width <= StandardLengths.Max())
                        )
                    {
                        break;
                    }
                }

                double distanse1 = ZoneSolution.CalculateDistanceBetweenZones(solution.Zones[idx1], solution.Zones[idx2]);
                if ((distanse1 > 3) ||
                    (solution.Zones[idx1].Boundary.Height + solution.Zones[idx2].Boundary.Height > StandardLengths.Max()) ||
                    (solution.Zones[idx1].Boundary.Width + solution.Zones[idx2].Boundary.Width > StandardLengths.Max())
                    )
                {
                    return;
                }

                //Если зоны имеют перескающиеся узлы и не могут быть покрыты одной зоной – мутация пропускается


                if (idx1 != idx2)
                {

                    var mergedNodes = solution.Zones[idx1].Nodes.Union(solution.Zones[idx2].Nodes).ToList();
                    var mergedBoundary = Rectangle.Union(solution.Zones[idx1].Boundary, solution.Zones[idx2].Boundary);

                    //Создаем зону

                    var mergedZone = CreateOptimalZone(mergedNodes, mergedBoundary);

                    if (mergedZone != null)
                    {
                        mergedZone.ZoneID = solution.Zones[idx1].ZoneID;
                        solution.Zones.RemoveAt(Math.Max(idx1, idx2));
                        solution.Zones.RemoveAt(Math.Min(idx1, idx2));
                        solution.Zones.Add(mergedZone);
                    }
                }
            }

            // 2. Разделение крупной зоны (10% вероятности)
            if (Random.NextDouble() < 1)
            {
                var largeZone = solution.Zones.OrderByDescending(s => s.TotalCost).FirstOrDefault(z =>
                    z.Boundary.Width > 3 || z.Boundary.Height > 3);
                if (largeZone != null)
                {
                    solution.Zones.Remove(largeZone);
                    solution.Zones.AddRange(SplitZone(largeZone));
                }
            }
        }

        private void MergeAllOverlappingZones(ReinforcementSolution solution)
        {
            bool merged;
            do
            {
                merged = false;

                // Группируем зоны по плитам для более эффективной обработки
                var zonesBySlab = solution.Zones.GroupBy(z => z.ZoneID);

                foreach (var slabGroup in zonesBySlab)
                {
                    var zones = slabGroup.ToList();

                    for (int i = 0; i < zones.Count; i++)
                    {
                        for (int j = i + 1; j < zones.Count; j++)
                        {
                            if (zones[i].Boundary.Intersects(zones[j].Boundary))
                            {
                                // Объединяем перекрывающиеся зоны
                                var mergedNodes = solution.Zones[i].Nodes.Union(solution.Zones[j].Nodes).ToList();
                                var mergedBoundary = Rectangle.Union(solution.Zones[i].Boundary, solution.Zones[j].Boundary);

                                //Создаем зону

                                var mergedZone = CreateOptimalZone(mergedNodes, mergedBoundary);

                                if (mergedZone != null)
                                {
                                    solution.Zones.Remove(zones[i]);
                                    solution.Zones.Remove(zones[j]);
                                    solution.Zones.Add(mergedZone);
                                    merged = true;
                                    break;
                                }
                            }
                        }
                        if (merged) break;
                    }
                    if (merged) break;
                }
            } while (merged); // Повторяем, пока есть что объединять
        }

        private List<ZoneSolution> SplitZone(ZoneSolution zone)
        {
            var newZones = new List<ZoneSolution>();
            double maxBarLength = StandardLengths.Max() / 1000;

            // Разделяем по большей стороне
            bool splitX = zone.Boundary.Width > zone.Boundary.Height;
            double splitCoord = splitX
                ? zone.Boundary.X + zone.Boundary.Width / 2
                : zone.Boundary.Y + zone.Boundary.Height / 2;

            var group1 = zone.Nodes
                .Where(n => splitX ? n.X < splitCoord : n.Y < splitCoord)
                .ToList();

            var group2 = zone.Nodes
                .Except(group1)
                .ToList();

            if (group1.Count > 0)
            {
                var boundary1 = CalculateGroupBoundary(group1);
                var zone1 = CreateOptimalZone(group1, boundary1);
                zone1.ZoneID = zone.ZoneID;
                if (zone1 != null) newZones.Add(zone1);
            }

            if (group2.Count > 0)
            {
                var boundary2 = CalculateGroupBoundary(group2);
                var zone2 = CreateOptimalZone(group2, boundary2);
                zone2.ZoneID = zone.ZoneID;
                if (zone2 != null) newZones.Add(zone2);
            }

            return newZones.Count > 0 ? newZones : new List<ZoneSolution> { zone };
        }

        private Rectangle CalculateGroupBoundary(List<Node> nodes)
        {
            double minX = nodes.Min(n => n.X) - 0.05;
            double minY = nodes.Min(n => n.Y) - 0.05;
            double maxX = nodes.Max(n => n.X) + 0.05;
            double maxY = nodes.Max(n => n.Y) + 0.05;

            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        private ReinforcementSolution CreateMinimalCoverageSolution(List<List<Node>> slabsNodes)
        {
            var solution = new ReinforcementSolution { Zones = new List<ZoneSolution>() };

            int floorid = -1;
            foreach (var nodes in slabsNodes)
            {
                floorid += 1;

                foreach (var node in nodes)
                {
                    var boundary = new Rectangle(node.X - 0.2, node.Y - 0.2, 0.2, 0.2);
                    var zone = CreateOptimalZone(new List<Node> { node }, boundary);
                    if (zone != null)
                    {
                        zone.ZoneID = floorid;
                        solution.Zones.Add(zone);
                    }
                }

            }

            return solution;
        }


        private List<ReinforcementSolution> GetBestUniqueSolutions(
            List<ReinforcementSolution> solutions,
            int count)
        {
            var ordered = solutions
                .OrderBy(s => s.FitnesCost)          // Главный критерий - общая стоимость
                .ThenBy(s => s.Zones.Count)          // Меньше зон - лучше
                //.ThenBy(s => s.Zones.Count(z => z.Nodes.Count <= 1)) // Меньше одиночных зон
                //.ThenBy(s => -s.Zones.Average(z => z.Nodes.Count))   // Больше узлов в среднем по зоне
                .ToList();

            var result = new List<ReinforcementSolution>();

            foreach (ReinforcementSolution sol in ordered)
            {
                bool dup = false;
                //Удаляем дубликаты
                foreach (ReinforcementSolution tmpsol in result)
                {
                    if (tmpsol.TotalCost == sol.TotalCost && tmpsol.Zones.Count == sol.Zones.Count)
                    {
                        dup = true;
                        break;
                    }
                }

                if (!dup)
                    result.Add(sol);


            }

            return result.Take(NumOfSol).ToList();
        }

        private string GetSolutionSignature(ReinforcementSolution solution)
        {
            return string.Join("|", solution.Zones
                .OrderBy(z => z.Boundary.X)
                .ThenBy(z => z.Boundary.Y)
                .Select(z => $"{z.Rebar.Diameter}@{z.Spacing}:{z.Boundary.Width}x{z.Boundary.Height}"));
        }

        private double OptimizeRebarLength(double requiredLength, List<double> standardLengths)
        {
            return standardLengths
                .Where(l => l >= requiredLength * 1000)
                .OrderBy(l => l - requiredLength * 1000)
                .FirstOrDefault();
        }

        private ZoneSolution CreateOptimalZone(List<Node> nodes, Rectangle boundary)
        {
            double requiredAs = nodes.Max(n => new[] { n.As1X, n.As2X, n.As3Y, n.As4Y }.Max()) * 100;

            var validConfigs = AvailableRebars
                .SelectMany(r => r.AvailableSpacings
                    .Select(s => new {
                        Rebar = r,
                        Spacing = s,
                        Area = Math.PI * r.Diameter * r.Diameter / 4 * (1000 / s)
                    }))
                .Where(x => x.Area >= requiredAs)
                .OrderBy(x => x.Area)
                .Select(x => CreateZoneWithConfig(nodes, boundary, x.Rebar, x.Spacing))
                .Where(z => z != null);

            //validConfigs.OrderBy(z => z.TotalCost).FirstOrDefault().Boundary.Width += validConfigs.OrderBy(z => z.TotalCost).FirstOrDefault().overHeat;
            //validConfigs.OrderBy(z => z.TotalCost).FirstOrDefault().Boundary.Height += validConfigs.OrderBy(z => z.TotalCost).FirstOrDefault().overHeat;

            if (validConfigs == null)
                return null;

            return validConfigs.OrderBy(z => z.TotalCost).FirstOrDefault();
        }

        private ZoneSolution CreateZoneWithConfig(List<Node> nodes, Rectangle boundary,
            RebarConfig rebar, double spacing)
        {
            if (boundary.Height < GetOverlapLength(rebar.Diameter))
                boundary.Height = GetOverlapLength(rebar.Diameter);

            if (boundary.Width < GetOverlapLength(rebar.Diameter))
                boundary.Width = GetOverlapLength(rebar.Diameter);

            if (boundary.Width > (StandardLengths.Max() / 1000) || boundary.Height > (StandardLengths.Max() / 1000))
                return null;

            double Height = boundary.Height;
            double Width = boundary.Width;

            double xBarsCount = Math.Max(Math.Ceiling(Height * 1000 / spacing), MinRebarPerDirection);
            double yBarsCount = Math.Max(Math.Ceiling(Width * 1000 / spacing), MinRebarPerDirection);

            double xBarLength = OptimizeRebarLength(Width, StandardLengths);
            double yBarLength = OptimizeRebarLength(Height, StandardLengths);

            double areaPerMeter = Math.PI * Math.Pow(rebar.Diameter, 2) / 4 * (1000 / spacing);

            double totalXLength = xBarsCount * xBarLength / 1000;
            double totalYLength = yBarsCount * yBarLength / 1000;
            double totalLength = totalXLength + totalYLength;

            double totalCost = totalLength * rebar.PricePerMeter;

            return new ZoneSolution
            {

                Nodes = nodes,
                Rebar = rebar,
                Spacing = spacing,
                Boundary = boundary,
                TotalCost = totalCost,
                TotalLength = totalLength,
                AreaX = areaPerMeter,
                AreaY = areaPerMeter,
                XBarsCount = xBarsCount,
                YBarsCount = yBarsCount,
                XBarLength = Width,
                YBarLength = Height,
                StandardLengthX = xBarLength,
                StandardLengthY = yBarLength,
                overHeat = GetOverlapLength(rebar.Diameter)
            };
        }

        private double GetOverlapLength(double rebarDiameter)
        {
            return 22 * rebarDiameter / 1000; // Переводим мм в метры
        }

        public static List<Opening> GetOpeningsFromRevit(List<Floor> floors)
        {
            var openings = new List<Opening>();
            if (floors == null || floors.Count == 0)
                return openings;

            Document doc = Command.uiDoc.Document;

            Options geomOptions = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            try
            {
                for (int slabId = 0; slabId < floors.Count; slabId++)
                {
                    var floor = floors[slabId];
                    using (GeometryElement geomElem = floor.get_Geometry(geomOptions))
                    {
                        if (geomElem == null) continue;

                        foreach (GeometryObject geomObj in geomElem)
                        {
                            if (geomObj is Solid solid && solid.Volume > 0)
                            {
                                foreach (Face face in solid.Faces)
                                {
                                    if (face is PlanarFace planarFace)
                                    {
                                        var curveLoops = planarFace.GetEdgesAsCurveLoops();
                                        if (curveLoops.Count < 2) continue;

                                        for (int i = 1; i < curveLoops.Count; i++)
                                        {
                                            var loop = curveLoops[i];
                                            var polygon = loop.Select(c => c.GetEndPoint(0)).ToList();
                                            var bbox = GetLoopBoundingBox(loop);

                                            if (bbox.Width < 0.1 || bbox.Height < 0.1)
                                                continue;

                                            openings.Add(new Opening
                                            {
                                                SlabId = slabId,
                                                Polygon = polygon,
                                                Boundary = bbox
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при получении отверстий: {ex.Message}");
            }

            return openings;
        }

        private static Rectangle GetLoopBoundingBox(CurveLoop loop)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (Curve curve in loop)
            {
                var points = curve.Tessellate();
                foreach (XYZ point in points)
                {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                }
            }

            return new Rectangle(
                minX - 0.05,
                minY - 0.05,
                (maxX - minX) + 0.1,
                (maxY - minY) + 0.1
            );
        }
    }

    public class ReinforcementSolution
    {
        public List<ZoneSolution> Zones { get; set; } = new List<ZoneSolution>();
        public double TotalCost { get; private set; }

        //Цена со штрафом, используем для оценки приспособленности
        public double FitnesCost { get; set; }
        
        public double TotalRebarLength => Zones.Sum(z => z.TotalLength);

        public void UpdateTotalCost()
        {
            TotalCost = Zones.Sum(z => z.TotalCost);
        }

        public ReinforcementSolution ShallowCopy()
        {
            return new ReinforcementSolution
            {
                Zones = new List<ZoneSolution>(this.Zones),
                TotalCost = this.TotalCost,
                FitnesCost = this.FitnesCost
            };
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (var zone in Zones.OrderBy(z => z.GetHashCode()))
                {
                    hash = hash * 23 + zone.GetHashCode();
                }
                return hash;
            }
        }
    }

    public class SpatialGrid<T> where T : Node
    {
        private readonly double _cellSize;
        private readonly Dictionary<(int, int), List<T>> _grid = new Dictionary<(int, int), List<T>>();

        public SpatialGrid(IEnumerable<T> items, double cellSize)
        {
            _cellSize = cellSize;
            foreach (var item in items)
            {
                var cell = GetCell(item.X, item.Y);
                if (!_grid.ContainsKey(cell))
                {
                    _grid[cell] = new List<T>();
                }
                _grid[cell].Add(item);
            }
        }

        public IEnumerable<T> GetNearbyItems(Node point, double radius)
        {
            var centerCell = GetCell(point.X, point.Y);
            int radiusInCells = (int)Math.Ceiling(radius / _cellSize);

            for (int dx = -radiusInCells; dx <= radiusInCells; dx++)
            {
                for (int dy = -radiusInCells; dy <= radiusInCells; dy++)
                {
                    var cell = (centerCell.Item1 + dx, centerCell.Item2 + dy);
                    if (_grid.TryGetValue(cell, out var items))
                    {
                        foreach (var item in items)
                        {
                            if (Distance(point, item) <= radius)
                            {
                                yield return item;
                            }
                        }
                    }
                }
            }
        }

        private (int, int) GetCell(double x, double y)
        {
            return ((int)(x / _cellSize), (int)(y / _cellSize));
        }

        private static double Distance(Node a, Node b)
        {
            return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        }
    }

    public class ZoneSolution
    {
        public List<Node> Nodes { get; set; }
        public RebarConfig Rebar { get; set; }
        public double Spacing { get; set; }
        public double StandardLengthX { get; set; }
        public double StandardLengthY { get; set; }
        public Rectangle Boundary { get; set; }
        public double TotalCost { get; set; }
        public double TotalLength { get; set; }
        public double AreaX { get; set; }
        public double AreaY { get; set; }
        public double XBarsCount { get; set; }
        public double YBarsCount { get; set; }
        public double XBarLength { get; set; }
        public double YBarLength { get; set; }
        public int ZoneID { get; set; }
        public double overHeat { get; set; }

        public void CopyFrom(ZoneSolution other)
        {
            this.Nodes = other.Nodes;
            this.Rebar = other.Rebar;
            this.Spacing = other.Spacing;
            this.StandardLengthX = other.StandardLengthX;
            this.StandardLengthY = other.StandardLengthY;
            this.Boundary = other.Boundary;
            this.TotalCost = other.TotalCost;
            this.TotalLength = other.TotalLength;
            this.AreaX = other.AreaX;
            this.AreaY = other.AreaY;
            this.XBarsCount = other.XBarsCount;
            this.YBarsCount = other.YBarsCount;
            this.XBarLength = other.XBarLength;
            this.YBarLength = other.YBarLength;
        }

        public static double CalculateDistanceBetweenZones(ZoneSolution zone1, ZoneSolution zone2)
        {
            // Если зоны пересекаются - расстояние 0
            if (zone1.Boundary.Intersects(zone2.Boundary))
                return 0;

            // Вычисляем горизонтальное и вертикальное расстояния
            double dx = Math.Max(0,
                Math.Max(zone1.Boundary.X - zone2.Boundary.X - zone2.Boundary.Width,
                        zone2.Boundary.X - zone1.Boundary.X - zone1.Boundary.Width));

            double dy = Math.Max(0,
                Math.Max(zone1.Boundary.Y - zone2.Boundary.Y - zone2.Boundary.Height,
                        zone2.Boundary.Y - zone1.Boundary.Y - zone1.Boundary.Height));

            // Евклидово расстояние
            return Math.Sqrt(dx * dx + dy * dy);
        }


        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Rebar?.GetHashCode() ?? 0;
                hash = hash * 23 + Spacing.GetHashCode();
                hash = hash * 23 + Boundary.GetHashCode();
                return hash;
            }
        }
    }

    public class Rectangle
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public Rectangle() { }

        public bool Intersects(Rectangle other)
        {
            // Проверяем отсутствие пересечения по оси X
            if (this.X + this.Width < other.X || other.X + other.Width < this.X)
                return false;

            // Проверяем отсутствие пересечения по оси Y
            if (this.Y + this.Height < other.Y || other.Y + other.Height < this.Y)
                return false;

            // Если оба условия не выполнились - прямоугольники пересекаются
            return true;
        }

        public static Rectangle Union(Rectangle a, Rectangle b)
        {
            double minX = Math.Min(a.X, b.X);
            double minY = Math.Min(a.Y, b.Y);
            double maxX = Math.Max(a.X + a.Width, b.X + b.Width);
            double maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);

            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        public Rectangle(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(double x, double y)
        {
            return x >= X && x <= X + Width && y >= Y && y <= Y + Height;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + X.GetHashCode();
                hash = hash * 23 + Y.GetHashCode();
                hash = hash * 23 + Width.GetHashCode();
                hash = hash * 23 + Height.GetHashCode();
                return hash;
            }
        }
    }

    public class ZoneComparer : IEqualityComparer<ZoneSolution>
    {
        public bool Equals(ZoneSolution x, ZoneSolution y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            return x.Rebar.Diameter == y.Rebar.Diameter &&
                   x.Spacing == y.Spacing &&
                   Math.Abs(x.Boundary.Width - y.Boundary.Width) < 0.01 &&
                   Math.Abs(x.Boundary.Height - y.Boundary.Height) < 0.01;
        }

        public int GetHashCode(ZoneSolution obj)
        {
                return (((((17 * 23 + (int)obj.Rebar.Diameter) * 23 + 
                (int)obj.Spacing)) * 23 + 
                (int)Math.Round(obj.Boundary.Width, 2)) * 23 +  
                (int)Math.Round(obj.Boundary.Height, 2));
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class PlanarVisualizationHandler : IExternalEventHandler
    {
        public ReinforcementSolution Solution { get; set; }
        public List<Floor> Floors { get; set; }
        public static UIDocument UiDoc { get; set; }

        public void Execute(UIApplication app)
        {
            UiDoc = app.ActiveUIDocument;
            var doc = UiDoc.Document;

            try
            {
                if (Floors == null || Floors.Count == 0)
                    return;

                // Получаем уровень из первой плиты
                var level = doc.GetElement(Floors[0].LevelId) as Level;
                if (level == null) return;

                // Получаем или создаем план
                ViewPlan viewPlan = GetOrCreatePlanView(doc, level);
                if (viewPlan == null) return;

                // Переключаемся на план
                UiDoc.ActiveView = viewPlan;

                using (Transaction t = new Transaction(doc, "2D Визуализация армирования"))
                {
                    t.Start();

                    // Удаляем ВСЕ кривые на этом виде (используем CurveElement вместо DetailCurve)
                    var allCurves = new FilteredElementCollector(doc, viewPlan.Id)
                        .OfClass(typeof(CurveElement))
                        .Where(e => e is DetailCurve) // Фильтруем только DetailCurve
                        .Select(e => e.Id)
                        .ToList();

                    var allTextNotes = new FilteredElementCollector(doc, viewPlan.Id)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>()
                        .ToList();

                    var textNotesToDelete = allTextNotes
                        .Where(tn => tn.Text.StartsWith("r"))
                        .Select(tn => tn.Id)
                        .ToList();

                    doc.Delete(textNotesToDelete);

                    if (allCurves.Count > 0)
                    {
                        doc.Delete(allCurves);
                    }

                    // Если нужно создать новые элементы
                    if (Solution != null && Solution.Zones != null)
                    {
                        int num = 1;
                        foreach (var zone in Solution.Zones.Where(z => z?.Nodes?.Count > 0))
                        {
                            VisualizeZone(doc, viewPlan, zone, Floors[zone.Nodes[0].SlabId], num);
                            num++;
                        }
                    }

                    t.Commit();
                }

                UiDoc.RefreshActiveView();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", ex.ToString());
            }
        }

        private ViewPlan GetOrCreatePlanView(Document doc, Level level)
        {
            // Поиск существующего плана
            ViewPlan viewPlan = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.GenLevel?.Id == level.Id);

            if (viewPlan != null) return viewPlan;

            // Создание нового плана
            using (Transaction t = new Transaction(doc, "Создание плана"))
            {
                t.Start();

                ViewFamilyType viewType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);

                if (viewType != null)
                {
                    viewPlan = ViewPlan.Create(doc, viewType.Id, level.Id);
                    viewPlan.Name = $"План армирования {level.Name}";
                }

                t.Commit();
            }

            return viewPlan;
        }

        private void VisualizeZone(Document doc, ViewPlan view, ZoneSolution zone, Floor floor, int num)
        {
            double elevation = (doc.GetElement(floor.LevelId) as Level)?.Elevation ?? 0;
            ElementId floorId = floor.Id;

            CreateZoneBoundary(doc, view, zone, elevation, floorId);
            CreateRebarVisualization(doc, view, zone, elevation, floorId);
            CreateZoneAnnotations(doc, view, zone, elevation, num);
        }

        private void CreateZoneAnnotations(Document doc, ViewPlan view, ZoneSolution zone, double elevation, int num)
        {
            try
            { 

                // 1. Создаем текст с параметрами зоны
                string annotationText = "r" + num.ToString();

                // 2. Вычисляем позицию текста (центр зоны)
                XYZ textPosition = new XYZ(
                    zone.Boundary.X + zone.Boundary.Width / 2,
                    zone.Boundary.Y + zone.Boundary.Height / 2,
                    elevation
                );

                // 3. Создаем текстовую аннотацию
                TextNoteOptions options = new TextNoteOptions
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    TypeId = GetDefaultTextNoteTypeId(doc)
                };

                TextNote textNote = TextNote.Create(doc, view.Id, textPosition, annotationText, options);

                textNote.Name = "Reinforcement_" + num.ToString();

                Parameter textHeightParam = textNote.get_Parameter(BuiltInParameter.TEXT_SIZE);
                textHeightParam?.Set(0.0115);

                // 4. Настройка внешнего вида
                OverrideGraphicSettings ogs = new OverrideGraphicSettings()
                    .SetProjectionLineColor(new Color(0, 0, 0))
                    .SetProjectionLineWeight(1);

                view.SetElementOverrides(textNote.Id, ogs);

                
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка создания аннотации: {ex.Message}");
            }
        }

        private ElementId GetDefaultTextNoteTypeId(Document doc)
        {
            // Ищем стандартный тип текста "3.5mm Arial"
            TextNoteType textType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name.Contains("3.5") || t.Name.Contains("Arial"));

            return textType?.Id ?? doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        }

        private void CreateZoneBoundary(Document doc, ViewPlan view, ZoneSolution zone, double elevation, ElementId floorId)
        {
            try
            {
                List<Curve> curves = new List<Curve>();
                double xMin = zone.Boundary?.X ?? 0;
                double yMin = zone.Boundary?.Y ?? 0;
                double xMax = xMin + (zone.Boundary?.Width ?? 0);
                double yMax = yMin + (zone.Boundary?.Height ?? 0);

                // Границы зоны с небольшим отступом
                double offset = 0.05;
                curves.Add(Line.CreateBound(new XYZ(xMin - offset, yMin - offset, elevation), new XYZ(xMax + offset, yMin - offset, elevation)));
                curves.Add(Line.CreateBound(new XYZ(xMax + offset, yMin - offset, elevation), new XYZ(xMax + offset, yMax + offset, elevation)));
                curves.Add(Line.CreateBound(new XYZ(xMax + offset, yMax + offset, elevation), new XYZ(xMin - offset, yMax + offset, elevation)));
                curves.Add(Line.CreateBound(new XYZ(xMin - offset, yMax + offset, elevation), new XYZ(xMin - offset, yMin - offset, elevation)));

                foreach (Curve curve in curves)
                {
                    // Создаем кривую с использованием CurveElement
                    CurveElement curveElement = doc.Create.NewDetailCurve(view, curve);

                    // Маркируем элемент
                    // curveElement.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                    //     .Set($"Reinforcement_Zone_{floorId.IntegerValue}");

                    // Настройка отображения
                    OverrideGraphicSettings ogs = new OverrideGraphicSettings()
                        .SetProjectionLineColor(new Color(0, 255, 0))
                        .SetProjectionLineWeight(1);

                    view.SetElementOverrides(curveElement.Id, ogs);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка создания границы", ex.Message);
            }
        }

        private void CreateRebarVisualization(Document doc, ViewPlan view, ZoneSolution zone, double elevation, ElementId floorId)
        {
            try
            {
                if (zone.Boundary == null || zone.Rebar == null) return;

                double spacing = zone.Spacing / 1000.0; // Переводим в метры
                List<Curve> curves = new List<Curve>();

                // Вертикальные стержни
                double offset = ((zone.Boundary.Height / spacing) - Math.Truncate(zone.Boundary.Height / spacing)) * spacing / 2;
                for (double y = zone.Boundary.Y; y <= zone.Boundary.Y + zone.Boundary.Height; y += spacing)
                {
                    curves.Add(Line.CreateBound(
                        new XYZ(zone.Boundary.X, y + offset, elevation),
                        new XYZ(zone.Boundary.X + zone.Boundary.Width, y + offset, elevation)));
                }

                // Горизонтальные стержни
                offset = ((zone.Boundary.Width / spacing) - Math.Truncate(zone.Boundary.Width / spacing)) * spacing / 2;
                for (double x = zone.Boundary.X; x <= zone.Boundary.X + zone.Boundary.Width; x += spacing)
                {
                    curves.Add(Line.CreateBound(
                        new XYZ(x + offset, zone.Boundary.Y, elevation),
                        new XYZ(x + offset, zone.Boundary.Y + zone.Boundary.Height, elevation)));
                }

                foreach (Curve curve in curves)
                {
                    // Создаем кривую с использованием CurveElement
                    CurveElement curveElement = doc.Create.NewDetailCurve(view, curve);

                    // Маркируем элемент
                    // curveElement.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                    //     .Set($"Reinforcement_Rebar_{floorId.IntegerValue}");

                    // Настройка отображения
                    OverrideGraphicSettings ogs = new OverrideGraphicSettings()
                        .SetProjectionLineColor(new Color(255, 0, 0))
                        .SetProjectionLineWeight(1);

                    view.SetElementOverrides(curveElement.Id, ogs);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка создания арматуры", ex.Message);
            }
        }

        public string GetName() => "2D Визуализация армирования";
    }

    [Transaction(TransactionMode.Manual)]
    public class PlanarCleanHandler : IExternalEventHandler
    {
        public static UIDocument UiDoc { get; set; }
        public List<Floor> Floors { get; set; }

        public void Execute(UIApplication app)
        {
            UiDoc = app.ActiveUIDocument;
            var doc = UiDoc.Document;

            var level = doc.GetElement(Floors[0].LevelId) as Level;
            if (level == null) return;

            ViewPlan viewPlan = new FilteredElementCollector(doc)
               .OfClass(typeof(ViewPlan))
               .Cast<ViewPlan>()
               .FirstOrDefault(v => !v.IsTemplate && v.GenLevel?.Id == level.Id);


            UiDoc.ActiveView = viewPlan;

            using (Transaction t = new Transaction(doc, "2D Визуализация армирования"))
            {
                t.Start();

                var allTextNotes = new FilteredElementCollector(doc, viewPlan.Id)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>()
                        .ToList();

                var textNotesToDelete = allTextNotes
                    .Where(tn => tn.Text.StartsWith("r"))
                    .Select(tn => tn.Id)
                    .ToList();

                doc.Delete(textNotesToDelete);

                // Удаляем ВСЕ кривые на этом виде (используем CurveElement вместо DetailCurve)
                var allCurves = new FilteredElementCollector(doc, viewPlan.Id)
                    .OfClass(typeof(CurveElement))
                    .Where(e => e is DetailCurve) // Фильтруем только DetailCurve
                    .Select(e => e.Id)
                    .ToList();

                if (allCurves.Count > 0)
                {
                    doc.Delete(allCurves);
                }

                t.Commit();
            }

            UiDoc.RefreshActiveView();
        }

        public string GetName() => "2D Удаление армирования";
    }


}