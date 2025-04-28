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

        private void CreateZoneAnnotation(Document doc, View3D view, ZoneSolution zone, double elevation, int zoneNumber)
        {
            XYZ center = new XYZ(
                zone.Boundary.X + zone.Boundary.Width / 2,
                zone.Boundary.Y + zone.Boundary.Height / 2,
                elevation + 0.2);

            string text = $"Зона {zoneNumber}\nØ{zone.Rebar.Diameter}@{zone.Spacing}";

            TextNote.Create(doc, view.Id, center, text, GetDefaultTextNoteType(doc).Id);
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

        private static GraphicsStyle GetLineStyle(Document doc, string styleName)
        {
            // Пытаемся найти существующий стиль
            GraphicsStyle style = new FilteredElementCollector(doc)
                .OfClass(typeof(GraphicsStyle))
                .Cast<GraphicsStyle>()
                .FirstOrDefault(gs => gs.Name == styleName);

            if (style == null)
            {
                // Используем стандартный стиль, если не нашли
                style = new FilteredElementCollector(doc)
                    .OfClass(typeof(GraphicsStyle))
                    .Cast<GraphicsStyle>()
                    .FirstOrDefault(gs => gs.GraphicsStyleCategory.Name == "Тонкие линии");
            }

            return style;
        }

        private TextNoteType GetDefaultTextNoteType(Document doc)
        {
            // Получаем первый доступный тип текстовой аннотации
            TextNoteType textType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .FirstElement() as TextNoteType;

            // Если нет существующих, создаем новый
            if (textType == null)
            {
                using (Transaction t = new Transaction(doc, "Create TextNote Type"))
                {
                    t.Start();
                    //textType = TextNoteType.Create(doc, "Аннотации армирования", null);
                    t.Commit();
                }
            }

            return textType;
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

    public class ReinforcementOptimizer
    {
        // Конфигурация алгоритма
        public List<RebarConfig> AvailableRebars { get; set; }
        public List<double> StandardLengths { get; set; }
        public double[] BasicReinforcement { get; set; }
        public List<Opening> Openings { get; set; } = new List<Opening>();
        public int PopulationSize { get; set; } = 50;
        public int Generations { get; set; } = 100;
        public double MutationRate { get; set; } = 0.3;
        public int EliteCount { get; set; } = 5;
        public double MinRebarPerDirection { get; set; } = 2;

        private static readonly Random Random = new Random();
        private List<Floor> _floors;
        private SpatialGrid<Node> _spatialGrid;

        public List<ReinforcementSolution> FindBestSolutions(List<List<Node>> slabsNodes, int solutionCount, List<Floor> floors)
        {
            _floors = floors;
            Openings = GetOpeningsFromRevit(floors);

            // Создаем пространственную сетку для быстрого поиска узлов
            var allNodes = slabsNodes.SelectMany(x => x).ToList();
            _spatialGrid = new SpatialGrid<Node>(allNodes, 2.0);

            // Инициализация популяции с гарантированным покрытием
            var population = InitializePopulationWithCoverage(slabsNodes);

            // Эволюционный процесс с проверкой покрытия
            for (int gen = 0; gen < Generations; gen++)
            {
                population = EvolvePopulationWithCoverageCheck(population, slabsNodes);
            }

            // Возврат лучших уникальных решений с минимальной ценой
            return GetBestUniqueSolutions(population, solutionCount);
        }

        // Добавлен метод для получения пустого прямоугольника
        private Rectangle GetEmptyRectangle()
        {
            return new Rectangle(0, 0, 0, 0);
        }

        private List<ReinforcementSolution> InitializePopulationWithCoverage(List<List<Node>> slabsNodes)
        {
            var population = new List<ReinforcementSolution>();
            var allNodes = slabsNodes.SelectMany(x => x).ToList();

            // 1. Минимальное покрытие (гарантированное)
            population.Add(CreateMinimalCoverageSolution(slabsNodes));

            // 2. Решение с крупными зонами (оптимизированное)
            population.Add(CreateOptimizedZonesSolution(population[0]));

            // 3. Случайные решения с гарантированным покрытием
            for (int i = 2; i < PopulationSize; i++)
            {
                population.Add(CreateRandomSolutionWithCoverage(slabsNodes));
            }

            return population;
        }

        private ReinforcementSolution CreateMinimalCoverageSolution(List<List<Node>> slabsNodes)
        {
            var solution = new ReinforcementSolution { Zones = new List<ZoneSolution>() };

            foreach (var nodes in slabsNodes)
            {
                foreach (var node in nodes)
                {
                    var boundary = new Rectangle(node.X - 0.2, node.Y - 0.2, 0.4, 0.4);
                    var zone = CreateOptimalZone(new List<Node> { node }, boundary);
                    if (zone != null) solution.Zones.Add(zone);
                }
            }

            return solution;
        }

        private ReinforcementSolution CreateRandomSolutionWithCoverage(List<List<Node>> slabsNodes)
        {
            var solution = new ReinforcementSolution { Zones = new List<ZoneSolution>() };
            var allNodes = slabsNodes.SelectMany(x => x).ToList();
            var remainingNodes = new List<Node>(allNodes);

            // 1. Сначала объединяем узлы в крупные кластеры
            while (remainingNodes.Count > 0)
            {
                var startNode = remainingNodes[Random.Next(remainingNodes.Count)];

                // Увеличиваем радиус поиска соседей для более крупных зон
                double searchRadius = 3.0 + Random.NextDouble() * 2.0; // 3-5 метров
                var nearbyNodes = _spatialGrid.GetNearbyItems(startNode, searchRadius)
                    .Where(n => remainingNodes.Contains(n))
                    .ToList();

                // Увеличиваем максимальный размер кластера
                int maxClusterSize = Random.Next(8, 25); // 8-24 узла
                var clusterNodes = nearbyNodes
                    .OrderBy(n => Distance(startNode, n))
                    .Take(maxClusterSize)
                    .ToList();

                // Создаем зону только если нашли достаточно узлов
                if (clusterNodes.Count >= 5) // Минимум 5 узлов для объединения
                {
                    var zone = CreateZoneWithHoleAvoidance(clusterNodes);
                    if (zone != null)
                    {
                        solution.Zones.Add(zone);
                        remainingNodes.RemoveAll(n => clusterNodes.Contains(n));
                        continue;
                    }
                }

                // Если не удалось создать большую зону, попробуем меньшую
                if (clusterNodes.Count >= 3)
                {
                    var smallerCluster = clusterNodes.Take(3).ToList();
                    var zone = CreateZoneWithHoleAvoidance(smallerCluster);
                    if (zone != null)
                    {
                        solution.Zones.Add(zone);
                        remainingNodes.RemoveAll(n => smallerCluster.Contains(n));
                        continue;
                    }
                }

                // В крайнем случае создаем зону для одного узла
                var singleZone = CreateSingleNodeZone(startNode);
                if (singleZone != null)
                {
                    solution.Zones.Add(singleZone);
                    remainingNodes.Remove(startNode);
                }
            }

            // 2. Оптимизируем полученные зоны, объединяя соседние
            OptimizeSolution(solution);

            return solution;
        }

        private void OptimizeSolution(ReinforcementSolution solution)
        {
            bool merged;
            do
            {
                merged = false;
                var zones = solution.Zones.OrderBy(z => z.Boundary.X).ToList();

                for (int i = 0; i < zones.Count; i++)
                {
                    for (int j = i + 1; j < zones.Count; j++)
                    {
                        var zone1 = zones[i];
                        var zone2 = zones[j];

                        // Проверяем расстояние между зонами
                        double distance = CalculateDistanceBetweenZones(zone1, zone2);

                        // Если зоны близко и не пересекаются с отверстиями
                        if (distance < 2.0 && ShouldMergeZones(zone1, zone2))
                        {
                            var mergedNodes = zone1.Nodes.Concat(zone2.Nodes).ToList();
                            var mergedBoundary = CalculateMergedBoundary(zone1.Boundary, zone2.Boundary);
                            var mergedZone = CreateOptimalZone(mergedNodes, mergedBoundary);

                            if (mergedZone != null && mergedZone.TotalCost < (zone1.TotalCost + zone2.TotalCost))
                            {
                                solution.Zones.Remove(zone1);
                                solution.Zones.Remove(zone2);
                                solution.Zones.Add(mergedZone);
                                merged = true;
                                break;
                            }
                        }
                    }
                    if (merged) break;
                }
            } while (merged);
        }

        private ZoneSolution CreateZoneWithHoleAvoidance(List<Node> nodes)
        {
            if (nodes == null || nodes.Count == 0) return null;

            var initialBoundary = CalculateTightBoundary(nodes);
            var intersectingOpenings = Openings
                .Where(o => RectanglesIntersect(initialBoundary, o.Boundary))
                .ToList();

            if (intersectingOpenings.Count == 0)
            {
                return CreateOptimalZone(nodes, initialBoundary);
            }

            var subClusters = SplitNodesAroundOpenings(nodes, initialBoundary, intersectingOpenings);
            if (subClusters.Count == 0) return null;

            var largestCluster = subClusters.OrderByDescending(c => c.Count).First();
            return CreateOptimalZone(largestCluster, CalculateTightBoundary(largestCluster));
        }

        private List<List<Node>> SplitNodesAroundOpenings(List<Node> nodes, Rectangle boundary, List<Opening> openings)
        {
            var result = new List<List<Node>>();
            var remainingNodes = new List<Node>(nodes);

            while (remainingNodes.Count > 0)
            {
                var startNode = remainingNodes[0];
                var cluster = new List<Node> { startNode };
                remainingNodes.RemoveAt(0);

                for (int i = 0; i < remainingNodes.Count;)
                {
                    var testNode = remainingNodes[i];
                    var testBoundary = CalculateTightBoundary(cluster.Concat(new[] { testNode }).ToList());

                    if (!openings.Any(o => RectanglesIntersect(testBoundary, o.Boundary)))
                    {
                        cluster.Add(testNode);
                        remainingNodes.RemoveAt(i);
                    }
                    else
                    {
                        i++;
                    }
                }

                if (cluster.Count > 0)
                {
                    result.Add(cluster);
                }
            }

            return result;
        }

        private Rectangle CalculateTightBoundary(IEnumerable<Node> nodes)
        {
            if (!nodes.Any()) return GetEmptyRectangle();

            double minX = nodes.Min(n => n.X);
            double maxX = nodes.Max(n => n.X);
            double minY = nodes.Min(n => n.Y);
            double maxY = nodes.Max(n => n.Y);

            return new Rectangle(
                minX - 0.1,
                minY - 0.1,
                maxX - minX + 0.2,
                maxY - minY + 0.2
            );
        }

        private List<ReinforcementSolution> EvolvePopulationWithCoverageCheck(
            List<ReinforcementSolution> population,
            List<List<Node>> slabsNodes)
        {
            var evaluated = population
                .AsParallel()
                .Select(s => {
                    s.UpdateTotalCost();
                    return s;
                })
                .OrderBy(s => s.TotalCost)
                .ToList();

            var newPopulation = evaluated.Take(EliteCount).ToList();

            while (newPopulation.Count < PopulationSize)
            {
                var parent1 = TournamentSelect(evaluated);
                var parent2 = TournamentSelect(evaluated);

                var offspring = CrossoverWithCoverage(parent1, parent2, slabsNodes);
                offspring = MutateWithCoverage(offspring, slabsNodes);

                newPopulation.Add(offspring);
            }

            return newPopulation;
        }

        private ReinforcementSolution TournamentSelect(List<ReinforcementSolution> population)
        {
            int tournamentSize = Math.Min(5, population.Count);
            return population
                .OrderBy(x => Random.Next())
                .Take(tournamentSize)
                .OrderBy(s => s.TotalCost)
                .First();
        }

        private ReinforcementSolution CrossoverWithCoverage(
            ReinforcementSolution parent1,
            ReinforcementSolution parent2,
            List<List<Node>> slabsNodes)
        {
            var child = new ReinforcementSolution { Zones = new List<ZoneSolution>() };

            var bestZones = parent1.Zones
                .Concat(parent2.Zones)
                .OrderBy(z => z.TotalCost / z.Nodes.Count)
                .Distinct(new ZoneComparer())
                .Take(parent1.Zones.Count)
                .ToList();

            var coveredNodes = new HashSet<Node>(bestZones.SelectMany(z => z.Nodes));
            var allNodes = slabsNodes.SelectMany(x => x).ToList();

            foreach (var node in allNodes.Except(coveredNodes))
            {
                var zone = CreateSingleNodeZone(node);
                if (zone != null) bestZones.Add(zone);
            }

            child.Zones = bestZones;
            return RemoveOverlappingZones(child);
        }

        private ReinforcementSolution MutateWithCoverage(
            ReinforcementSolution solution,
            List<List<Node>> slabsNodes)
        {
            if (Random.NextDouble() > MutationRate) return solution;

            var mutated = solution.ShallowCopy();
            var allNodes = slabsNodes.SelectMany(x => x).ToList();

            int mutationType = Random.Next(4);
            switch (mutationType)
            {
                case 0: MergeRandomZones(mutated); break;
                case 1: SplitRandomZone(mutated); break;
                case 2: ChangeRandomZoneRebar(mutated); break;
                case 3: OptimizeZoneSizes(mutated); break;
            }

            EnsureFullCoverage(mutated, slabsNodes);
            return mutated;
        }

        private void OptimizeZoneSizes(ReinforcementSolution solution)
        {
            foreach (var zone in solution.Zones)
            {
                var optimalLengthX = OptimizeRebarLength(zone.Boundary.Width, StandardLengths);
                var optimalLengthY = OptimizeRebarLength(zone.Boundary.Height, StandardLengths);

                if (optimalLengthX > 0 && optimalLengthY > 0)
                {
                    var newWidth = optimalLengthX / 1000;
                    var newHeight = optimalLengthY / 1000;

                    if (zone.Nodes.All(n =>
                        n.X >= zone.Boundary.X && n.X <= zone.Boundary.X + newWidth &&
                        n.Y >= zone.Boundary.Y && n.Y <= zone.Boundary.Y + newHeight))
                    {
                        zone.Boundary = new Rectangle(
                            zone.Boundary.X,
                            zone.Boundary.Y,
                            newWidth,
                            newHeight);

                        var updatedZone = CreateOptimalZone(zone.Nodes, zone.Boundary);
                        if (updatedZone != null)
                        {
                            zone.CopyFrom(updatedZone);
                        }
                    }
                }
            }
        }

        private List<ReinforcementSolution> GetBestUniqueSolutions(
            List<ReinforcementSolution> solutions,
            int count)
        {
            return solutions
                .AsParallel()
                .Select(s => {
                    s.UpdateTotalCost();
                    return s;
                })
                .GroupBy(s => GetSolutionSignature(s))
                .Select(g => g.OrderBy(s => s.TotalCost).First())
                .OrderBy(s => s.TotalCost)
                .Take(count)
                .ToList();
        }

        private string GetSolutionSignature(ReinforcementSolution solution)
        {
            return string.Join("|", solution.Zones
                .OrderBy(z => z.Boundary.X)
                .ThenBy(z => z.Boundary.Y)
                .Select(z => $"{z.Rebar.Diameter}@{z.Spacing}:{z.Boundary.Width}x{z.Boundary.Height}"));
        }

        private double Distance(Node a, Node b)
        {
            return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        }

        private bool RectanglesIntersect(Rectangle a, Rectangle b)
        {
            return a.X < b.X + b.Width &&
                   a.X + a.Width > b.X &&
                   a.Y < b.Y + b.Height &&
                   a.Y + a.Height > b.Y;
        }

        private double OptimizeRebarLength(double requiredLength, List<double> standardLengths)
        {
            return standardLengths
                .Where(l => l >= requiredLength * 1000)
                .OrderBy(l => l - requiredLength * 1000)
                .FirstOrDefault();
        }

        private ReinforcementSolution RemoveOverlappingZones(ReinforcementSolution solution)
        {
            var nonOverlapping = new List<ZoneSolution>();
            var sortedZones = solution.Zones.OrderBy(z => z.Boundary.X).ToList();

            foreach (var zone in sortedZones)
            {
                if (!nonOverlapping.Any(z => RectanglesIntersect(z.Boundary, zone.Boundary)))
                {
                    nonOverlapping.Add(zone);
                }
            }

            return new ReinforcementSolution { Zones = nonOverlapping };
        }

        private void EnsureFullCoverage(ReinforcementSolution solution, List<List<Node>> slabsNodes)
        {
            var allNodes = slabsNodes.SelectMany(x => x).ToList();
            var coveredNodes = solution.Zones.SelectMany(z => z.Nodes).Distinct().ToList();
            var uncoveredNodes = allNodes.Except(coveredNodes).ToList();

            foreach (var node in uncoveredNodes)
            {
                var boundary = new Rectangle(node.X - 0.2, node.Y - 0.2, 0.4, 0.4);
                var zone = CreateOptimalZone(new List<Node> { node }, boundary);
                if (zone != null) solution.Zones.Add(zone);
            }
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
                .Take(5)
                .Select(x => CreateZoneWithConfig(nodes, boundary, x.Rebar, x.Spacing))
                .Where(z => z != null);

            return validConfigs.OrderBy(z => z.TotalCost).FirstOrDefault();
        }

        private ZoneSolution CreateZoneWithConfig(List<Node> nodes, Rectangle boundary,
            RebarConfig rebar, double spacing)
        {
            double xBarsCount = Math.Max(Math.Ceiling(boundary.Height * 1000 / spacing), MinRebarPerDirection);
            double yBarsCount = Math.Max(Math.Ceiling(boundary.Width * 1000 / spacing), MinRebarPerDirection);

            double xBarLength = OptimizeRebarLength(boundary.Width, StandardLengths);
            double yBarLength = OptimizeRebarLength(boundary.Height, StandardLengths);

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
                XBarLength = boundary.Width,
                YBarLength = boundary.Height,
                StandardLengthX = xBarLength,
                StandardLengthY = yBarLength
            };
        }

        private ReinforcementSolution CreateOptimizedZonesSolution(ReinforcementSolution initialSolution)
        {
            var mergedSolution = initialSolution.ShallowCopy();
            bool mergedAny;

            do
            {
                mergedAny = false;

                for (int i = 0; i < mergedSolution.Zones.Count; i++)
                {
                    for (int j = i + 1; j < mergedSolution.Zones.Count; j++)
                    {
                        var zone1 = mergedSolution.Zones[i];
                        var zone2 = mergedSolution.Zones[j];

                        if (ShouldMergeZones(zone1, zone2))
                        {
                            var mergedNodes = zone1.Nodes.Concat(zone2.Nodes).ToList();
                            var mergedBoundary = CalculateMergedBoundary(zone1.Boundary, zone2.Boundary);
                            var mergedZone = CreateOptimalZone(mergedNodes, mergedBoundary);

                            if (mergedZone != null && mergedZone.TotalCost < (zone1.TotalCost + zone2.TotalCost))
                            {
                                mergedSolution.Zones.RemoveAt(j);
                                mergedSolution.Zones.RemoveAt(i);
                                mergedSolution.Zones.Add(mergedZone);
                                mergedAny = true;
                                break;
                            }
                        }
                    }
                    if (mergedAny) break;
                }
            } while (mergedAny);

            return mergedSolution;
        }

        private bool ShouldMergeZones(ZoneSolution zone1, ZoneSolution zone2)
        {
            // Проверяем схожесть требуемого армирования
            double maxLoad1 = zone1.Nodes.Max(n => new[] { n.As1X, n.As2X, n.As3Y, n.As4Y }.Max());
            double maxLoad2 = zone2.Nodes.Max(n => new[] { n.As1X, n.As2X, n.As3Y, n.As4Y }.Max());
            double loadDiff = Math.Abs(maxLoad1 - maxLoad2);

            // Проверяем пересечение с отверстиями
            var mergedBoundary = CalculateMergedBoundary(zone1.Boundary, zone2.Boundary);
            bool intersectsWithOpening = Openings.Any(o => RectanglesIntersect(mergedBoundary, o.Boundary));

            // Проверяем, что зоны относятся к одной плите
            bool sameSlab = zone1.Nodes.First().SlabId == zone2.Nodes.First().SlabId;

            return loadDiff < 1.0 && !intersectsWithOpening && sameSlab;
        }

        private Rectangle CalculateMergedBoundary(Rectangle a, Rectangle b)
        {
            double minX = Math.Min(a.X, b.X);
            double minY = Math.Min(a.Y, b.Y);
            double maxX = Math.Max(a.X + a.Width, b.X + b.Width);
            double maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);

            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        private double CalculateDistanceBetweenZones(ZoneSolution zone1, ZoneSolution zone2)
        {
            var center1 = new System.Windows.Point(
                zone1.Boundary.X + zone1.Boundary.Width / 2,
                zone1.Boundary.Y + zone1.Boundary.Height / 2);

            var center2 = new System.Windows.Point(
                zone2.Boundary.X + zone2.Boundary.Width / 2,
                zone2.Boundary.Y + zone2.Boundary.Height / 2);

            return Math.Sqrt(Math.Pow(center1.X - center2.X, 2) + Math.Pow(center1.Y - center2.Y, 2));
        }

        private ZoneSolution CreateSingleNodeZone(Node node)
        {
            var boundary = new Rectangle(node.X - 0.2, node.Y - 0.2, 0.4, 0.4);
            return CreateOptimalZone(new List<Node> { node }, boundary);
        }

        private void MergeRandomZones(ReinforcementSolution solution)
        {
            if (solution.Zones.Count < 2) return;

            int idx1 = Random.Next(solution.Zones.Count);
            int idx2 = FindNearestZone(solution.Zones, idx1);

            if (idx2 >= 0)
            {
                var mergedNodes = solution.Zones[idx1].Nodes
                    .Concat(solution.Zones[idx2].Nodes)
                    .ToList();

                var mergedBoundary = CalculateBoundary(mergedNodes);
                var mergedZone = CreateOptimalZone(mergedNodes, mergedBoundary);

                if (mergedZone != null)
                {
                    solution.Zones.RemoveAt(Math.Max(idx1, idx2));
                    solution.Zones.RemoveAt(Math.Min(idx1, idx2));
                    solution.Zones.Add(mergedZone);
                }
            }
        }

        private void SplitRandomZone(ReinforcementSolution solution)
        {
            if (solution.Zones.Count == 0) return;

            int idx = Random.Next(solution.Zones.Count);
            var zone = solution.Zones[idx];

            if (zone.Nodes.Count > 3)
            {
                var split = SplitNodes(zone.Nodes);
                solution.Zones.RemoveAt(idx);

                foreach (var nodes in split)
                {
                    var boundary = CalculateBoundary(nodes);
                    var newZone = CreateOptimalZone(nodes, boundary);
                    if (newZone != null) solution.Zones.Add(newZone);
                }
            }
        }

        private Rectangle CalculateBoundary(List<Node> nodes)
        {
            return new Rectangle(
                nodes.Min(n => n.X) - 0.1,
                nodes.Min(n => n.Y) - 0.1,
                nodes.Max(n => n.X) - nodes.Min(n => n.X) + 0.2,
                nodes.Max(n => n.Y) - nodes.Min(n => n.Y) + 0.2
            );
        }

        private void ChangeRandomZoneRebar(ReinforcementSolution solution)
        {
            if (solution.Zones.Count == 0 || AvailableRebars.Count == 0) return;

            int idx = Random.Next(solution.Zones.Count);
            var newRebar = AvailableRebars[Random.Next(AvailableRebars.Count)];
            var newSpacing = newRebar.AvailableSpacings[Random.Next(newRebar.AvailableSpacings.Count)];

            var newZone = CreateZoneWithConfig(
                solution.Zones[idx].Nodes,
                solution.Zones[idx].Boundary,
                newRebar,
                newSpacing);

            if (newZone != null)
            {
                solution.Zones[idx] = newZone;
            }
        }

        private int FindNearestZone(List<ZoneSolution> zones, int referenceIndex)
        {
            if (zones.Count < 2) return -1;

            var reference = zones[referenceIndex];
            double minDistance = double.MaxValue;
            int nearestIndex = -1;

            for (int i = 0; i < zones.Count; i++)
            {
                if (i == referenceIndex) continue;

                double distance = CalculateDistanceBetweenZones(reference, zones[i]);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        private List<List<Node>> SplitNodes(List<Node> nodes)
        {
            if (nodes.Count <= 1)
                return new List<List<Node>> { nodes };

            var sortedNodes = (Random.NextDouble() < 0.5)
                ? nodes.OrderBy(n => n.X).ToList()
                : nodes.OrderBy(n => n.Y).ToList();

            int splitIndex = nodes.Count / 2 + 1;

            var firstPart = new List<Node>();
            var secondPart = new List<Node>();

            for (int i = 0; i < sortedNodes.Count; i++)
            {
                if (i < splitIndex)
                    firstPart.Add(sortedNodes[i]);
                else
                    secondPart.Add(sortedNodes[i]);
            }

            return new List<List<Node>> { firstPart, secondPart };
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
                TotalCost = this.TotalCost
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

                    if (allCurves.Count > 0)
                    {
                        doc.Delete(allCurves);
                    }

                    // Если нужно создать новые элементы
                    if (Solution != null && Solution.Zones != null)
                    {
                        foreach (var zone in Solution.Zones.Where(z => z?.Nodes?.Count > 0))
                        {
                            VisualizeZone(doc, viewPlan, zone, Floors[zone.Nodes[0].SlabId]);
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

        private void VisualizeZone(Document doc, ViewPlan view, ZoneSolution zone, Floor floor)
        {
            double elevation = (doc.GetElement(floor.LevelId) as Level)?.Elevation ?? 0;
            ElementId floorId = floor.Id;

            CreateZoneBoundary(doc, view, zone, elevation, floorId);
            CreateRebarVisualization(doc, view, zone, elevation, floorId);
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