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

    // Классы для генетического алгоритма
    public class RebarConfig
    {
        public double Diameter { get; set; }
        public List<double> AvailableSpacings { get; set; }
        public double PricePerMeter { get; set; }
    }

    public class Opening
    {
        public Rectangle Boundary { get; set; }
        public int SlabId { get; set; }
    }

    public class Node
    {
        public string Type { get; set; }
        public int Number { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double ZCenter { get; set; }
        public double ZMin { get; set; }
        public double As1X { get; set; }
        public double As2X { get; set; }
        public double As3Y { get; set; }
        public double As4Y { get; set; }
        public int SlabId { get; set; }
    }

    public class ReinforcementSolution
    {
        public List<ZoneSolution> Zones { get; set; }
        public double TotalCost => Zones.Sum(z => z.TotalCost);

        public ReinforcementSolution ShallowCopy(ReinforcementSolution source)
        {
            return new ReinforcementSolution
            {
                Zones = new List<ZoneSolution>(source.Zones)
            };
        }
    }

    public class ZoneSolution
    {
        public List<Node> Nodes { get; set; }
        public RebarConfig Rebar { get; set; }
        public double Spacing { get; set; }
        public double StandardLength { get; set; }
        public Rectangle Boundary { get; set; }
        public double TotalCost { get; set; }
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
        private static readonly Random Random = new Random();

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
                                            var bbox = GetLoopBoundingBox(loop);

                                            // Фильтруем слишком маленькие отверстия
                                            if (bbox.Width < 0.1 || bbox.Height < 0.1)
                                                continue;

                                            openings.Add(new Opening
                                            {
                                                SlabId = slabId,
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

        // Основной метод оптимизации
        public List<ReinforcementSolution> FindBestSolutions(List<List<Node>> slabsNodes, int solutionCount)
        {
            // 1. Инициализация популяции
            var population = InitializePopulation(slabsNodes);

            // 2. Эволюционный процесс
            for (int gen = 0; gen < Generations; gen++)
            {
                population = EvolvePopulation(population, slabsNodes);
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
                    // Разные стратегии кластеризации для разнообразия
                    List<List<Node>> zones;
                    if (i % 3 == 0)
                        zones = ClusterByGrid(nodes);
                    else if (i % 3 == 1)
                        zones = ClusterByDistance(nodes);
                    else
                        zones = ClusterByLoad(nodes);

                    // Оптимизация с учетом отверстий
                    zones = OptimizeZoneShapes(zones, Openings);

                    foreach (var zoneNodes in zones)
                    {
                        var zone = OptimizeZone(zoneNodes);
                        if (zone != null) solution.Zones.Add(zone);
                    }
                }

                if (solution.Zones.Count > 0)
                    population.Add(solution);
            }

            return population;
        }

        // Метод разделения кластера на подкластеры
        private List<List<Node>> SplitCluster(List<Node> cluster, List<Opening> openings)
        {
            if (cluster.Count <= 3) // Не разделяем маленькие кластеры
                return new List<List<Node>> { cluster };

            // Разделяем по координате X
            var sorted = cluster.OrderBy(n => n.X).ToList();
            int mid = sorted.Count / 2;

            var left = sorted.Take(mid).ToList();
            var right = sorted.Skip(mid).ToList();

            return new List<List<Node>> { left, right };
        }

        // Оптимизация отдельной зоны
        private ZoneSolution OptimizeZone(List<Node> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return null;

            var boundary = CalculateBoundary(nodes);
            double requiredAs = nodes.Max(n => Math.Max(Math.Max(n.As1X, n.As2X), Math.Max(n.As3Y, n.As4Y)));

            var bestConfig = AvailableRebars
                .SelectMany(r => r.AvailableSpacings.Select(s => new {
                    Rebar = r,
                    Spacing = s,
                    Area = Math.PI * r.Diameter * r.Diameter / 4 * (1000 / s)
                }))
                .Where(x => x.Area >= requiredAs)
                .OrderBy(x => x.Rebar.PricePerMeter)
                .FirstOrDefault();

            if (bestConfig == null)
                return null;

            double length = StandardLengths.First() / 1000;
            double xBars = Math.Ceiling((boundary.Height / bestConfig.Spacing) / 1000);
            double yBars = Math.Ceiling((boundary.Width / bestConfig.Spacing) / 1000);
            double totalCost = (xBars + yBars) * length * bestConfig.Rebar.PricePerMeter;

            return new ZoneSolution
            {
                Nodes = nodes,
                Rebar = bestConfig.Rebar,
                Spacing = bestConfig.Spacing,
                Boundary = boundary,
                TotalCost = totalCost
            };
        }

        // Метод выбора родителя для скрещивания (турнирная селекция)
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

        // Кластеризация по нагрузке
        private List<List<Node>> ClusterByLoad(List<Node> nodes)
        {
            // Разделяем узлы на группы по требованию армирования
            double threshold = BasicReinforcement.Average() * 1.5;
            var highLoad = nodes.Where(n =>
                n.As1X > threshold ||
                n.As2X > threshold ||
                n.As3Y > threshold ||
                n.As4Y > threshold).ToList();

            var lowLoad = nodes.Except(highLoad).ToList();

            var result = new List<List<Node>>();
            if (highLoad.Count > 0) result.Add(highLoad);
            if (lowLoad.Count > 0) result.Add(lowLoad);

            return result;
        }

        private List<ReinforcementSolution> EvolvePopulation(List<ReinforcementSolution> population,
                                                           List<List<Node>> slabsNodes)
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
                offspring = Mutate(offspring, slabsNodes, Openings);

                if (offspring?.Zones?.Count > 0)
                    newPopulation.Add(offspring);
            }

            return newPopulation;
        }

        #region Методы кластеризации
        private List<List<Node>> ClusterByGrid(List<Node> nodes)
        {
            double gridSize = 3.0;
            var grid = new Dictionary<(int, int), List<Node>>();

            foreach (var node in nodes)
            {
                int xCell = (int)(node.X / gridSize);
                int yCell = (int)(node.Y / gridSize);
                var key = (xCell, yCell);

                if (!grid.ContainsKey(key)) grid[key] = new List<Node>();
                grid[key].Add(node);
            }

            return grid.Values.ToList();
        }

        private List<List<Node>> ClusterByDistance(List<Node> nodes)
        {
            double maxDistance = 2.5;
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

        private List<List<Node>> SplitClusterAroundOpenings(List<Node> cluster, Rectangle boundary,
                                                  List<Opening> openings)
        {
            // Преобразуем Opening в Rectangle
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
                    .Where(n => IsPointInRectangle(n.X, n.Y, area))
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

                    // Разбиваем область на 4 возможных прямоугольника вокруг отверстия
                    newAreas.AddRange(SplitRectangleAroundHole(area, hole));
                }

                allowedAreas = newAreas
                    .Where(a => a.Width > 0.1 && a.Height > 0.1) // Фильтруем слишком маленькие области
                    .ToList();
            }

            return allowedAreas;
        }

        private List<Rectangle> SplitRectangleAroundHole(Rectangle original, Rectangle hole)
        {
            var result = new List<Rectangle>();

            // 1. Левая часть (если отверстие не у левого края)
            if (hole.X > original.X)
            {
                result.Add(new Rectangle
                {
                    X = original.X,
                    Y = original.Y,
                    Width = hole.X - original.X,
                    Height = original.Height
                });
            }

            // 2. Правая часть (если отверстие не у правого края)
            if (hole.X + hole.Width < original.X + original.Width)
            {
                result.Add(new Rectangle
                {
                    X = hole.X + hole.Width,
                    Y = original.Y,
                    Width = original.X + original.Width - (hole.X + hole.Width),
                    Height = original.Height
                });
            }

            // 3. Верхняя часть (между левой и правой частями)
            if (hole.Y > original.Y)
            {
                result.Add(new Rectangle
                {
                    X = Math.Max(original.X, hole.X),
                    Y = original.Y,
                    Width = Math.Min(original.X + original.Width, hole.X + hole.Width) -
                           Math.Max(original.X, hole.X),
                    Height = hole.Y - original.Y
                });
            }

            // 4. Нижняя часть (между левой и правой частями)
            if (hole.Y + hole.Height < original.Y + original.Height)
            {
                result.Add(new Rectangle
                {
                    X = Math.Max(original.X, hole.X),
                    Y = hole.Y + hole.Height,
                    Width = Math.Min(original.X + original.Width, hole.X + hole.Width) -
                           Math.Max(original.X, hole.X),
                    Height = original.Y + original.Height - (hole.Y + hole.Height)
                });
            }

            return result.Where(r => r.Width > 0.1 && r.Height > 0.1).ToList();
        }

        private bool IsPointInRectangle(double x, double y, Rectangle rect)
        {
            return x >= rect.X &&
                   x <= rect.X + rect.Width &&
                   y >= rect.Y &&
                   y <= rect.Y + rect.Height;
        }
        private ReinforcementSolution Crossover(ReinforcementSolution parent1, ReinforcementSolution parent2,
                                              List<List<Node>> slabsNodes)
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

        private ReinforcementSolution Mutate(ReinforcementSolution solution, List<List<Node>> slabsNodes,
                                          List<Opening> openings)
        {
            var mutated = solution.ShallowCopy(solution);

            if (Random.NextDouble() > MutationRate || mutated.Zones.Count == 0)
                return mutated;

            int zoneIndex = Random.Next(mutated.Zones.Count);
            var zone = mutated.Zones[zoneIndex];

            switch (Random.Next(3))
            {
                case 0: // Изменение размеров
                    mutated.Zones[zoneIndex] = ResizeZone(zone, openings);
                    break;

                case 1: // Разделение зоны
                    mutated.Zones.RemoveAt(zoneIndex);
                    var split = SplitCluster(zone.Nodes, openings);
                    mutated.Zones.AddRange(split.Select(OptimizeZone));
                    break;
            }

            return mutated;
        }

        private ZoneSolution ResizeZone(ZoneSolution zone, List<Opening> openings)
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

                if (openings.Any(o => RectanglesIntersect(newBoundary, o.Boundary)))
                    continue;

                var testSolution = CreateZoneSolution(zone.Nodes, newBoundary);
                if (testSolution.TotalCost < minCost)
                {
                    minCost = testSolution.TotalCost;
                    bestBoundary = newBoundary;
                }
            }

            return CreateZoneSolution(zone.Nodes, bestBoundary);
        }
        #endregion

        #region Вспомогательные методы
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
                if (testSolution.TotalCost < bestSolution.TotalCost)
                    bestSolution = testSolution;
            }

            return bestSolution;
        }

        private ZoneSolution CreateZoneSolution(List<Node> nodes, Rectangle boundary)
        {
            double requiredAs = nodes
                .Select(n => new[] { n.As1X, n.As2X, n.As3Y, n.As4Y }.Max())
                .Max();

            var bestConfig = AvailableRebars
                .SelectMany(r => r.AvailableSpacings
                    .Select(s => new {
                        Rebar = r,
                        Spacing = s,
                        Area = Math.PI * r.Diameter * r.Diameter / 4 * (1000 / s)
                    }))
                .Where(x => x.Area >= requiredAs)
                .OrderBy(x => x.Rebar.PricePerMeter)
                .FirstOrDefault();

            if (bestConfig == null) return null;

            double length = StandardLengths.First();
            double xBars = Math.Ceiling(boundary.Height / bestConfig.Spacing);
            double yBars = Math.Ceiling(boundary.Width / bestConfig.Spacing);
            double totalCost = (xBars + yBars) * length * bestConfig.Rebar.PricePerMeter;

            return new ZoneSolution
            {
                Nodes = nodes,
                Rebar = bestConfig.Rebar,
                Spacing = bestConfig.Spacing,
                Boundary = boundary,
                TotalCost = totalCost
            };
        }

        private List<ReinforcementSolution> GetUniqueSolutions(List<ReinforcementSolution> solutions, int count)
        {
            return solutions
                .GroupBy(s => string.Join("|", s.Zones
                    .OrderBy(z => z.Boundary.X)
                    .Select(z => $"{z.Rebar.Diameter}@{z.Spacing}")))
                .Select(g => g.First())
                .OrderBy(s => s.TotalCost)
                .Take(count)
                .ToList();
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
    }
}

