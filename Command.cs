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

        public static VisualizationHandler VisualizationHandler { get; private set; }
        public static ExternalEvent VisualizationEvent { get; private set; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UserControl1 view = new UserControl1();
            uiDoc = commandData.Application.ActiveUIDocument;
            VisualizationHandler = new VisualizationHandler();
            VisualizationEvent = ExternalEvent.Create(VisualizationHandler);

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
    public class VisualizationHandler : IExternalEventHandler
    {
        public ReinforcementSolution Solution { get; set; }
        public int SolutionIndex { get; set; }
        public UIDocument UiDoc { get; set; }
        public List<Floor> Floors { get; set; }

        public void Execute(UIApplication app)
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            Document doc = uiDoc.Document;

            using (Transaction trans = new Transaction(doc, "3D Reinforcement Visualization"))
            {
                try
                {
                    trans.Start();

                    // Очищаем предыдущую визуализацию
                    CleanPreviousVisualization(doc);

                    // Создаем 3D вид
                    View3D view3D = GetOrCreate3DView(doc);
                    uiDoc.ActiveView = view3D;

                    // Визуализируем каждую зону
                    for (int i = 0; i < Solution.Zones.Count; i++)
                    {
                        VisualizeZone(doc, view3D, Solution.Zones[i], i + 1);
                    }

                    trans.Commit();

                    // Обновляем вид
                    uiDoc.RefreshActiveView();
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Ошибка визуализации", ex.Message);
                    trans.RollBack();
                }
            }

        }

        private void VisualizeZone(Document doc, View3D view, ZoneSolution zone, int zoneNumber)
        {
            if (zone.Nodes == null || zone.Nodes.Count == 0) return;

            // Получаем соответствующую плиту
            Floor floor = Floors[zone.Nodes[0].SlabId];
            Level level = doc.GetElement(floor.LevelId) as Level;
            double elevation = level.Elevation;

            // Создаем границу зоны
            CreateZoneBoundary(doc, view, zone, elevation);

            // Визуализируем арматуру
            CreateRebarVisualization(doc, view, zone, elevation);

            // Добавляем текстовую аннотацию
            CreateZoneAnnotation(doc, view, zone, elevation, zoneNumber);
        }

        private void CreateZoneBoundary(Document doc, View3D view, ZoneSolution zone, double elevation)
        {
            // Создаем линии границы зоны
            XYZ p1 = new XYZ(zone.Boundary.X, zone.Boundary.Y, elevation + 0.1);
            XYZ p2 = new XYZ(zone.Boundary.X + zone.Boundary.Width, zone.Boundary.Y, elevation + 0.1);
            XYZ p3 = new XYZ(zone.Boundary.X + zone.Boundary.Width, zone.Boundary.Y + zone.Boundary.Height, elevation + 0.1);
            XYZ p4 = new XYZ(zone.Boundary.X, zone.Boundary.Y + zone.Boundary.Height, elevation + 0.1);

            // Создаем 4 линии границы
            CreateModelCurve(doc, Line.CreateBound(p1, p2), "ZoneBorderStyle");
            CreateModelCurve(doc, Line.CreateBound(p2, p3), "ZoneBorderStyle");
            CreateModelCurve(doc, Line.CreateBound(p3, p4), "ZoneBorderStyle");
            CreateModelCurve(doc, Line.CreateBound(p4, p1), "ZoneBorderStyle");
        }

        private void CreateRebarVisualization(Document doc, View3D view, ZoneSolution zone, double elevation)
        {
            double spacing = zone.Spacing / 1000.0; // мм -> м

            // Вертикальные стержни
            for (double y = zone.Boundary.Y; y <= zone.Boundary.Y + zone.Boundary.Height; y += spacing)
            {
                XYZ start = new XYZ(zone.Boundary.X, y, elevation + 0.1);
                XYZ end = new XYZ(zone.Boundary.X + zone.Boundary.Width, y, elevation + 0.1);
                CreateModelCurve(doc, Line.CreateBound(start, end), "RebarStyle");
            }

            // Горизонтальные стержни
            for (double x = zone.Boundary.X; x <= zone.Boundary.X + zone.Boundary.Width; x += spacing)
            {
                XYZ start = new XYZ(x, zone.Boundary.Y, elevation + 0.1);
                XYZ end = new XYZ(x, zone.Boundary.Y + zone.Boundary.Height, elevation + 0.1);
                CreateModelCurve(doc, Line.CreateBound(start, end), "RebarStyle");
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

        private void CleanPreviousVisualization(Document doc)
        {
            // Удаляем все ModelCurve из проекта
            ICollection<ElementId> curves = new FilteredElementCollector(doc)
                .OfClass(typeof(ModelCurve))
                .ToElementIds();

            if (curves.Count > 0)
            {
                doc.Delete(curves);
            }
        }

        private View3D GetOrCreate3DView(Document doc)
        {
            View3D view = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == "Armature Visualization");

            if (view == null)
            {
                using (Transaction t = new Transaction(doc, "Create 3D View"))
                {
                    t.Start();
                    view = View3D.CreateIsometric(doc,
                        new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .First(x => x.ViewFamily == ViewFamily.ThreeDimensional).Id);
                    view.Name = "Armature Visualization";
                    t.Commit();
                }
            }

            return view;
        }

        private ModelCurve CreateModelCurve(Document doc, Curve curve, string styleName)
        {
            // Создаем плоскость эскиза на уровне XY
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            // Создаем модельную кривую
            ModelCurve modelCurve = doc.Create.NewModelCurve(curve, sketchPlane);

            // Применяем стиль
            GraphicsStyle style = GetLineStyle(doc, styleName);
            if (style != null)
            {
                modelCurve.LineStyle = style;
            }

            // Настраиваем цвет
            Color color = styleName == "RebarStyle" ? new Color(255, 0, 0) : new Color(0, 255, 0);
            OverrideGraphicSettings settings = new OverrideGraphicSettings()
                .SetProjectionLineColor(color)
                .SetProjectionLineWeight(2);

            doc.ActiveView.SetElementOverrides(modelCurve.Id, settings);

            return modelCurve;
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

    public class ReinforcementSolution
    {
        public List<ZoneSolution> Zones { get; set; }
        public double TotalCost => Zones.Sum(z => z.TotalCost);
        public double TotalRebarLength => Zones.Sum(z => z.TotalLength);

        public ReinforcementSolution ShallowCopy()
        {
            return new ReinforcementSolution
            {
                Zones = new List<ZoneSolution>(this.Zones)
            };
        }
    }

    public class ZoneSolution
    {
        public List<Node> Nodes { get; set; }
        public RebarConfig Rebar { get; set; }
        public double Spacing { get; set; } // Шаг арматуры (мм)
        public double StandardLength { get; set; } // Длина стержней (мм)
        public Rectangle Boundary { get; set; }
        public double TotalCost { get; set; }
        public double TotalLength { get; set; } // Общая длина арматуры (м)
        public double AreaX { get; set; } // Площадь арматуры в X направлении (мм²/м)
        public double AreaY { get; set; } // Площадь арматуры в Y направлении (мм²/м)
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
    }

    public class ReinforcementOptimizer
    {
        // Конфигурация алгоритма
        public List<RebarConfig> AvailableRebars { get; set; }
        public List<double> StandardLengths { get; set; }
        public double[] BasicReinforcement { get; set; }
        public List<Opening> Openings { get; set; } = new List<Opening>();
        public int PopulationSize { get; set; } = 50;
        public int Generations { get; set; } = 1000;
        public double MutationRate { get; set; } = 0.3;
        public int EliteCount { get; set; } = 5;
        public double MinRebarPerDirection { get; set; } = 2; // Минимум 2 стержня в каждом направлении

        private static readonly Random Random = new Random();

        // Основной метод оптимизации
        public List<ReinforcementSolution> FindBestSolutions(List<List<Node>> slabsNodes, int solutionCount)
        {
            // 1. Инициализация популяции
            var population = InitializePopulation(slabsNodes);

            // 2. Эволюционный процесс
            for (int gen = 0; gen < Generations; gen++)
            {
                population = EvolvePopulation(population, slabsNodes);

                // Гарантируем покрытие всех узлов в каждом решении
                foreach (var solution in population)
                {
                    EnsureFullCoverage(solution, slabsNodes);
                }

                Debug.WriteLine($"Generation {gen}: Best cost = {population[0].TotalCost}");
            }

            // 3. Возврат лучших уникальных решений
            return GetUniqueSolutions(population, solutionCount);
        }

        private List<ReinforcementSolution> InitializePopulation(List<List<Node>> slabsNodes)
        {
            var population = new List<ReinforcementSolution>();

            for (int i = 0; i < PopulationSize; i++)
            {
                var solution = new ReinforcementSolution { Zones = new List<ZoneSolution>() };

                foreach (var nodes in slabsNodes.Where(n => n?.Count > 0))
                {
                    List<List<Node>> clusters;
                    switch (i % 3)
                    {
                        case 0:
                            clusters = ClusterByProximity(nodes, 2.0);
                            break;
                        case 1:
                            clusters = ClusterByGrid(nodes, 3.0);
                            break;
                        default:
                            clusters = ClusterByLoad(nodes);
                            break;
                    }

                    clusters = OptimizeZoneShapes(clusters, Openings);

                    foreach (var cluster in clusters)
                    {
                        var zone = OptimizeZone(cluster);
                        if (zone != null && IsValidZone(zone))
                        {
                            solution.Zones.Add(zone);
                        }
                    }
                }

                population.Add(solution);
            }

            return population;
        }

        private bool IsValidZone(ZoneSolution zone)
        {
            // Проверка минимального количества стержней в каждом направлении
            double xBars = Math.Ceiling(zone.Boundary.Height / (zone.Spacing / 1000));
            double yBars = Math.Ceiling(zone.Boundary.Width / (zone.Spacing / 1000));

            return xBars >= MinRebarPerDirection && yBars >= MinRebarPerDirection;
        }

        private List<ReinforcementSolution> EvolvePopulation(List<ReinforcementSolution> population, List<List<Node>> slabsNodes)
        {
            // 1. Оценка и сортировка
            var evaluated = population
                .Where(s => s != null && s.Zones != null)
                .OrderBy(s => s.TotalCost)
                .ToList();

            // 2. Отбор элитных решений
            var newPopulation = evaluated.Take(EliteCount).ToList();

            // 3. Генерация потомков
            while (newPopulation.Count < PopulationSize)
            {
                var parent1 = SelectParent(evaluated);
                var parent2 = SelectParent(evaluated);

                var offspring = Crossover(parent1, parent2, slabsNodes);
                offspring = Mutate(offspring, slabsNodes);

                if (offspring?.Zones?.Count > 0)
                    newPopulation.Add(offspring);
            }

            return newPopulation;
        }

        private ReinforcementSolution SelectParent(List<ReinforcementSolution> population)
        {
            int tournamentSize = Math.Min(5, population.Count);
            var candidates = population
                .OrderBy(x => Random.Next())
                .Take(tournamentSize)
                .OrderBy(s => s.TotalCost)
                .ToList();

            return candidates.First();
        }

        private ReinforcementSolution Crossover(ReinforcementSolution parent1, ReinforcementSolution parent2, List<List<Node>> slabsNodes)
        {
            var child = new ReinforcementSolution { Zones = new List<ZoneSolution>() };

            for (int i = 0; i < slabsNodes.Count; i++)
            {
                if (Random.NextDouble() < 0.5)
                    child.Zones.AddRange(parent1.Zones.Where(z => z.Nodes[0].SlabId == i));
                else
                    child.Zones.AddRange(parent2.Zones.Where(z => z.Nodes[0].SlabId == i));
            }

            return child;
        }

        private ReinforcementSolution Mutate(ReinforcementSolution solution, List<List<Node>> slabsNodes)
        {
            var mutated = solution.ShallowCopy();

            if (Random.NextDouble() > MutationRate || mutated.Zones.Count == 0)
                return mutated;

            int zoneIndex = Random.Next(mutated.Zones.Count);
            var zone = mutated.Zones[zoneIndex];

            switch (Random.Next(3))
            {
                case 0: // Изменение размеров зоны
                    mutated.Zones[zoneIndex] = ResizeZone(zone);
                    break;

                case 1: // Разделение зоны
                    mutated.Zones.RemoveAt(zoneIndex);
                    var split = SplitCluster(zone.Nodes);
                    mutated.Zones.AddRange(split.Select(OptimizeZone).Where(z => z != null && IsValidZone(z)));
                    break;

                case 2: // Изменение типа арматуры
                    var newRebar = GetRandomRebarConfig();
                    if (newRebar != null)
                    {
                        var newZone = CreateZoneSolution(zone.Nodes, zone.Boundary, newRebar);
                        if (newZone != null && IsValidZone(newZone))
                        {
                            mutated.Zones[zoneIndex] = newZone;
                        }
                    }
                    break;
            }

            return mutated;
        }

        private RebarConfig GetRandomRebarConfig()
        {
            if (AvailableRebars == null || AvailableRebars.Count == 0)
                return null;

            return AvailableRebars[Random.Next(AvailableRebars.Count)];
        }

        private ZoneSolution ResizeZone(ZoneSolution zone)
        {
            Rectangle bestBoundary = zone.Boundary;
            double minCost = zone.TotalCost;

            // Тестируем 5 вариантов изменения размера
            for (int i = 0; i < 5; i++)
            {
                double expand = (Random.NextDouble() * 2) - 0.5; // [-0.5, 1.5]

                var newBoundary = new Rectangle(
                    zone.Boundary.X - expand / 2,
                    zone.Boundary.Y - expand / 2,
                    zone.Boundary.Width + expand,
                    zone.Boundary.Height + expand
                );

                // Проверяем пересечение с отверстиями
                if (Openings.Any(o => RectanglesIntersect(newBoundary, o.Boundary)))
                    continue;

                var testSolution = CreateZoneSolution(zone.Nodes, newBoundary, zone.Rebar);
                if (testSolution != null && testSolution.TotalCost < minCost)
                {
                    minCost = testSolution.TotalCost;
                    bestBoundary = newBoundary;
                }
            }

            return CreateZoneSolution(zone.Nodes, bestBoundary, zone.Rebar);
        }

        private List<List<Node>> SplitCluster(List<Node> cluster)
        {
            if (cluster.Count <= 3) // Не разделяем маленькие кластеры
                return new List<List<Node>> { cluster };

            // Разделяем по координате X или Y
            if (Random.NextDouble() < 0.5)
            {
                var sorted = cluster.OrderBy(n => n.X).ToList();
                int mid = sorted.Count / 2;
                return new List<List<Node>> { sorted.Take(mid).ToList(), sorted.Skip(mid).ToList() };
            }
            else
            {
                var sorted = cluster.OrderBy(n => n.Y).ToList();
                int mid = sorted.Count / 2;
                return new List<List<Node>> { sorted.Take(mid).ToList(), sorted.Skip(mid).ToList() };
            }
        }

        private List<ReinforcementSolution> GetUniqueSolutions(List<ReinforcementSolution> solutions, int count)
        {
            return solutions
                .GroupBy(s => string.Join("|", s.Zones
                    .OrderBy(z => z.Boundary.X)
                    .ThenBy(z => z.Boundary.Y)
                    .Select(z => $"{z.Rebar.Diameter}@{z.Spacing}:{z.Boundary.Width}x{z.Boundary.Height}")))
                .Select(g => g.First())
                .OrderBy(s => s.TotalCost)
                .Take(count)
                .ToList();
        }

        #region Методы кластеризации
        private List<List<Node>> ClusterByProximity(List<Node> nodes, double maxDistance)
        {
            var clusters = new List<List<Node>>();
            var visited = new HashSet<Node>();

            foreach (var node in nodes.OrderBy(n => n.X))
            {
                if (visited.Contains(node)) continue;

                var cluster = new List<Node>();
                var queue = new Queue<Node>();
                queue.Enqueue(node);
                visited.Add(node);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    cluster.Add(current);

                    var neighbors = nodes
                        .Where(n => !visited.Contains(n) && Distance(current, n) <= maxDistance)
                        .ToList();

                    foreach (var neighbor in neighbors)
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                if (cluster.Count > 0)
                    clusters.Add(cluster);
            }

            return clusters;
        }

        private List<List<Node>> ClusterByGrid(List<Node> nodes, double gridSize)
        {
            var grid = new Dictionary<(int, int), List<Node>>();

            foreach (var node in nodes)
            {
                int xCell = (int)(node.X / gridSize);
                int yCell = (int)(node.Y / gridSize);
                var key = (xCell, yCell);

                if (!grid.ContainsKey(key)) grid[key] = new List<Node>();
                grid[key].Add(node);
            }

            return grid.Values.Where(c => c.Count >= 3).ToList();
        }

        private List<List<Node>> ClusterByLoad(List<Node> nodes)
        {
            if (nodes.Count == 0)
                return new List<List<Node>>();

            // 1. Пространственная кластеризация
            var spatialClusters = ClusterByProximity(nodes, maxDistance: 2.0);

            // 2. Разделение по нагрузке
            var result = new List<List<Node>>();
            foreach (var cluster in spatialClusters)
            {
                if (cluster.Count == 0) continue;

                double medianLoad = CalculateMedianLoad(cluster);

                var highLoadNodes = cluster
                    .Where(n => new[] { n.As1X, n.As2X, n.As3Y, n.As4Y }.Max() > medianLoad)
                    .ToList();

                var lowLoadNodes = cluster.Except(highLoadNodes).ToList();

                if (highLoadNodes.Count > 0) result.Add(highLoadNodes);
                if (lowLoadNodes.Count > 0) result.Add(lowLoadNodes);
            }

            // 3. Объединение мелких кластеров
            return MergeSmallClusters(result, minSize: 3);
        }

        private double CalculateMedianLoad(List<Node> nodes)
        {
            var loads = nodes
                .Select(n => new[] { n.As1X, n.As2X, n.As3Y, n.As4Y }.Max())
                .OrderBy(x => x)
                .ToList();

            int midIndex = loads.Count / 2;
            return (loads.Count % 2 != 0) ?
                loads[midIndex] :
                (loads[midIndex - 1] + loads[midIndex]) / 2.0;
        }

        private List<List<Node>> MergeSmallClusters(List<List<Node>> clusters, int minSize)
        {
            var merged = new List<List<Node>>();
            var largeClusters = clusters.Where(c => c.Count >= minSize).ToList();
            var smallClusters = clusters.Where(c => c.Count < minSize).ToList();

            merged.AddRange(largeClusters);

            foreach (var smallCluster in smallClusters)
            {
                var nearestCluster = FindNearestCluster(smallCluster, merged);

                if (nearestCluster != null && ShouldMerge(nearestCluster, smallCluster))
                {
                    var combinedCluster = nearestCluster.Concat(smallCluster).ToList();
                    var updatedCluster = RecalculateCluster(combinedCluster);
                    merged.Remove(nearestCluster);
                    merged.Add(updatedCluster);
                }
                else
                {
                    merged.Add(RecalculateCluster(smallCluster));
                }
            }

            return merged;
        }

        private bool ShouldMerge(List<Node> cluster1, List<Node> cluster2)
        {
            double loadDiff = Math.Abs(
                cluster1[0].MedianLoad - cluster2[0].MedianLoad);
            return loadDiff < 1.0;
        }

        private List<Node> RecalculateCluster(List<Node> cluster)
        {
            if (cluster == null || cluster.Count == 0)
                return cluster;

            var newBoundary = CalculateBoundary(cluster);
            double medianLoad = CalculateMedianLoad(cluster);

            foreach (var node in cluster)
            {
                node.Boundary = newBoundary;
                node.ClusterVersion++;
                node.MedianLoad = medianLoad;
            }

            return cluster;
        }

        private List<Node> FindNearestCluster(List<Node> cluster, List<List<Node>> existingClusters)
        {
            if (existingClusters == null || existingClusters.Count == 0)
                return null;

            double centerX = cluster.Average(n => n.X);
            double centerY = cluster.Average(n => n.Y);

            var nearest = existingClusters
                .Select(c => new
                {
                    Cluster = c,
                    Distance = Math.Sqrt(
                        Math.Pow(c.Average(n => n.X) - centerX, 2) +
                        Math.Pow(c.Average(n => n.Y) - centerY, 2))
                })
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            return nearest?.Distance <= 3.0 ? nearest.Cluster : null;
        }

        private List<List<Node>> OptimizeZoneShapes(List<List<Node>> clusters, List<Opening> openings)
        {
            var optimized = new List<List<Node>>();

            foreach (var cluster in clusters)
            {
                var boundary = CalculateBoundary(cluster);
                var subClusters = SplitClusterAroundOpenings(cluster, boundary, openings);

                foreach (var subCluster in subClusters)
                {
                    var optimizedZone = FindOptimalShape(subCluster, openings);
                    optimized.Add(optimizedZone.Nodes);
                }
            }

            return optimized;
        }

        private List<List<Node>> SplitClusterAroundOpenings(List<Node> cluster, Rectangle boundary, List<Opening> openings)
        {
            var intersecting = openings
                .Where(o => RectanglesIntersect(boundary, o.Boundary))
                .Select(o => o.Boundary)
                .ToList();

            if (!intersecting.Any())
                return new List<List<Node>> { cluster };

            var allowedAreas = CalculateAllowedAreas(boundary, intersecting);
            var result = new List<List<Node>>();

            foreach (var area in allowedAreas)
            {
                var nodesInArea = cluster
                    .Where(n => area.Contains(n.X, n.Y))
                    .ToList();

                if (nodesInArea.Count > 0)
                    result.Add(nodesInArea);
            }

            return result;
        }

        private List<Rectangle> CalculateAllowedAreas(Rectangle boundary, List<Rectangle> holes)
        {
            var allowedAreas = new List<Rectangle> { boundary };

            foreach (var hole in holes.OrderBy(h => h.X))
            {
                var newAreas = new List<Rectangle>();

                foreach (var area in allowedAreas)
                {
                    if (!RectanglesIntersect(area, hole))
                    {
                        newAreas.Add(area);
                        continue;
                    }

                    newAreas.AddRange(SplitRectangleAroundHole(area, hole));
                }

                allowedAreas = newAreas
                    .Where(a => a.Width > 0.1 && a.Height > 0.1)
                    .ToList();
            }

            return allowedAreas;
        }

        private List<Rectangle> SplitRectangleAroundHole(Rectangle original, Rectangle hole)
        {
            var result = new List<Rectangle>();

            if (hole.X > original.X)
            {
                result.Add(new Rectangle(
                    original.X,
                    original.Y,
                    hole.X - original.X,
                    original.Height
                ));
            }

            if (hole.X + hole.Width < original.X + original.Width)
            {
                result.Add(new Rectangle(
                    hole.X + hole.Width,
                    original.Y,
                    original.X + original.Width - (hole.X + hole.Width),
                    original.Height
                ));
            }

            if (hole.Y > original.Y)
            {
                result.Add(new Rectangle(
                    Math.Max(original.X, hole.X),
                    original.Y,
                    Math.Min(original.X + original.Width, hole.X + hole.Width) -
                    Math.Max(original.X, hole.X),
                    hole.Y - original.Y
                ));
            }

            if (hole.Y + hole.Height < original.Y + original.Height)
            {
                result.Add(new Rectangle(
                    Math.Max(original.X, hole.X),
                    hole.Y + hole.Height,
                    Math.Min(original.X + original.Width, hole.X + hole.Width) -
                    Math.Max(original.X, hole.X),
                    original.Y + original.Height - (hole.Y + hole.Height)
                ));
            }

            return result.Where(r => r.Width > 0.1 && r.Height > 0.1).ToList();
        }

        private ZoneSolution FindOptimalShape(List<Node> nodes, List<Opening> openings)
        {
            var boundary = CalculateBoundary(nodes);
            ZoneSolution bestSolution = CreateZoneSolution(nodes, boundary);

            // Тестируем варианты с разными отступами
            for (double padding = 0.5; padding <= 1.5; padding += 0.5)
            {
                var testBoundary = new Rectangle(
                    boundary.X - padding,
                    boundary.Y - padding,
                    boundary.Width + 2 * padding,
                    boundary.Height + 2 * padding
                );

                if (openings.Any(o => RectanglesIntersect(testBoundary, o.Boundary)))
                    continue;

                var testSolution = CreateZoneSolution(nodes, testBoundary);
                if (testSolution != null && testSolution.TotalCost < bestSolution.TotalCost)
                    bestSolution = testSolution;
            }

            return bestSolution;
        }
        #endregion

        #region Методы работы с зонами армирования
        private void EnsureFullCoverage(ReinforcementSolution solution, List<List<Node>> slabsNodes)
        {
            foreach (var nodes in slabsNodes)
            {
                var uncoveredNodes = GetUncoveredNodes(nodes, solution.Zones);
                if (uncoveredNodes.Count == 0)
                    continue;

                // Создаем дополнительные зоны для непокрытых узлов
                var newClusters = ClusterByProximity(uncoveredNodes, maxDistance: 1.5);
                foreach (var cluster in newClusters)
                {
                    var zone = OptimizeZone(cluster);
                    if (zone != null && IsValidZone(zone))
                    {
                        solution.Zones.Add(zone);
                    }
                }
            }
        }

        private List<Node> GetUncoveredNodes(List<Node> allNodes, List<ZoneSolution> zones)
        {
            var coveredNodes = new HashSet<Node>();
            foreach (var zone in zones)
            {
                foreach (var node in zone.Nodes)
                {
                    coveredNodes.Add(node);
                }
            }
            return allNodes.Where(n => !coveredNodes.Contains(n)).ToList();
        }

        private ZoneSolution OptimizeZone(List<Node> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return null;

            var boundary = CalculateBoundary(nodes);

            // Убедимся, что зона не пересекается с отверстиями
            if (Openings.Any(o => RectanglesIntersect(boundary, o.Boundary)))
            {
                // Если пересекается, разбиваем кластер на меньшие части
                var subClusters = SplitClusterAroundOpenings(nodes, boundary, Openings);
                if (subClusters.Count == 0)
                    return null;

                // Оптимизируем каждую подзону
                var bestZone = subClusters
                    .Select(OptimizeZone)
                    .Where(z => z != null)
                    .OrderBy(z => z.TotalCost)
                    .FirstOrDefault();

                return bestZone;
            }

            return CreateZoneSolution(nodes, boundary);
        }

        private ZoneSolution CreateZoneSolution(List<Node> nodes, Rectangle boundary, RebarConfig specificRebar = null)
        {
            if (nodes == null || nodes.Count == 0 || boundary.Width <= 0 || boundary.Height <= 0)
                return null;

            // Конвертируем требуемое армирование из см²/м в мм²/м
            double requiredAs = nodes
                .Select(n => new[] { n.As1X, n.As2X, n.As3Y, n.As4Y }.Max())
                .Max() * 100;

            // 1. Находим все возможные варианты арматуры, удовлетворяющие требованиям
            var possibleConfigs = new List<ZoneSolution>();

            // Перебираем все доступные длины стержней
            foreach (var length in StandardLengths)
            {
                // Перебираем все доступные конфигурации арматуры
                var rebarOptions = specificRebar != null
                    ? new List<RebarConfig> { specificRebar }
                    : AvailableRebars;

                foreach (var rebar in rebarOptions)
                {
                    foreach (var spacing in rebar.AvailableSpacings)
                    {
                        double areaPerMeter = Math.PI * rebar.Diameter * rebar.Diameter / 4 * (1000 / spacing);
                        if (areaPerMeter < requiredAs)
                            continue;

                        // Рассчитываем количество стержней в каждом направлении
                        double xBars = Math.Ceiling(boundary.Height / (spacing / 1000));
                        double yBars = Math.Ceiling(boundary.Width / (spacing / 1000));

                        // Проверяем минимальное количество стержней
                        if (xBars < MinRebarPerDirection || yBars < MinRebarPerDirection)
                            continue;

                        // Рассчитываем общую стоимость
                        double totalLength = (xBars + yBars) * (length / 1000);
                        double totalCost = totalLength * rebar.PricePerMeter;

                        possibleConfigs.Add(new ZoneSolution
                        {
                            Nodes = nodes,
                            Rebar = rebar,
                            Spacing = spacing,
                            StandardLength = length,
                            Boundary = boundary,
                            TotalCost = totalCost,
                            TotalLength = totalLength,
                            AreaX = areaPerMeter,
                            AreaY = areaPerMeter
                        });
                    }
                }
            }

            // 2. Если нашли подходящие варианты - возвращаем самый дешевый
            if (possibleConfigs.Count > 0)
                return possibleConfigs.OrderBy(c => c.TotalCost).First();

            // 3. Если не нашли - создаем минимально возможную зону с 2 стержнями
            return CreateMinimalZone(nodes, boundary, requiredAs);
        }

        private ZoneSolution CreateMinimalZone(List<Node> nodes, Rectangle boundary, double requiredAs)
        {
            // Находим минимальный диаметр, удовлетворяющий требованиям по площади
            var suitableRebars = AvailableRebars
                .SelectMany(r => r.AvailableSpacings.Select(s => new
                {
                    Rebar = r,
                    Spacing = s,
                    Area = Math.PI * r.Diameter * r.Diameter / 4 * (1000 / s)
                }))
                .Where(x => x.Area >= requiredAs)
                .OrderBy(x => x.Rebar.PricePerMeter)
                .ToList();

            if (suitableRebars.Count == 0)
                return null;

            // Берем самый дешевый вариант
            var bestConfig = suitableRebars.First();

            // Рассчитываем минимальные размеры зоны для 2 стержней
            double minWidth = bestConfig.Spacing / 1000 * (MinRebarPerDirection - 1);
            double minHeight = bestConfig.Spacing / 1000 * (MinRebarPerDirection - 1);

            // Корректируем границы зоны, если она слишком мала
            var adjustedBoundary = new Rectangle(
                boundary.X,
                boundary.Y,
                Math.Max(boundary.Width, minWidth),
                Math.Max(boundary.Height, minHeight)
            );

            // Используем минимальную длину стержней для экономии
            double length = StandardLengths.Min();

            // Рассчитываем стоимость (2 стержня в каждом направлении)
            double totalLength = MinRebarPerDirection * 2 * (length / 1000);
            double totalCost = totalLength * bestConfig.Rebar.PricePerMeter;

            return new ZoneSolution
            {
                Nodes = nodes,
                Rebar = bestConfig.Rebar,
                Spacing = bestConfig.Spacing,
                StandardLength = length,
                Boundary = adjustedBoundary,
                TotalCost = totalCost,
                TotalLength = totalLength,
                AreaX = bestConfig.Area,
                AreaY = bestConfig.Area
            };
        }
        #endregion

        #region Геометрические расчеты
        private Rectangle CalculateBoundary(List<Node> nodes)
        {
            double minX = nodes.Min(n => n.X);
            double maxX = nodes.Max(n => n.X);
            double minY = nodes.Min(n => n.Y);
            double maxY = nodes.Max(n => n.Y);

            return new Rectangle
            {
                X = minX - 0.1,
                Y = minY - 0.1,
                Width = maxX - minX + 0.2,
                Height = maxY - minY + 0.2
            };
        }

        private bool RectanglesIntersect(Rectangle a, Rectangle b)
        {
            return a.X < b.X + b.Width &&
                   a.X + a.Width > b.X &&
                   a.Y < b.Y + b.Height &&
                   a.Y + a.Height > b.Y;
        }

        private double Distance(Node a, Node b) =>
            Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        #endregion


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

                                        // Обрабатываем только внутренние контуры (отверстия)
                                        for (int i = 1; i < curveLoops.Count; i++)
                                        {
                                            var loop = curveLoops[i];
                                            var polygon = loop.Select(c => c.GetEndPoint(0)).ToList();
                                            var bbox = GetLoopBoundingBox(loop);

                                            // Фильтруем слишком маленькие отверстия
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

            // Добавляем небольшой запас (5 см)
            return new Rectangle(
                minX - 0.05,
                minY - 0.05,
                (maxX - minX) + 0.1,
                (maxY - minY) + 0.1
            );
        }
    }

}