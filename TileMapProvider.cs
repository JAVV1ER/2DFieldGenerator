using System;
using System.Collections;
using System.Collections.Generic;
using Systems.EnemyControlSystem.Messages;
using System.Linq;
using Systems.StatsDisplay.Contract;
using MessageBus.Contract;
using MineBombers.Content.Scripts.FieldGenerator;
using Scripts.Core.Configs;
using UnityEngine;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;

public class TileMapProvider : MonoBehaviour
{
    public int seed;
    public Vector3Int mapSize;
    private GameObject _tMapDefaultPrefab;
    private GameObject _tMapTreasurePrefab;
    private List<TileMapsLayers> _tileMapLayersList;
    private TileBase _warFogMapTileBase;
    private TileMapTreasure _treasureConfig;
    private TileMapHeal _healConfig;
    private Grid _mainGrid;
    private int _lastSortingLayer;
    private List<TileBase> _explodeFrames;
    private TileBase _borderTile;
    private IStatusDisplay _statusDisplay;

    private Tilemap _explodeEffectLayer;
    private Tilemap _treasureTileMapLayer;
    private Tilemap _healTileMapLayer;
    private Tilemap _borderLayer;

    private Tilemap _warFogTileMap;
    private IHasEnemiesParams _enemiesConfig;

    private Tilemap _destroyableTilemap;
    private List<HashSet<Vector3Int>> _destroyedHashList = new();
    private HashSet<Vector3Int> _hashSetOfFog = new();

    private Camera _camera;
    private IMessageBus _messageBus;

    private GameObject _nuclearEffectPrefab;


    public void Init(
        Vector3Int mapSizeMax,
        GameObject tileMapPrefab,
        List<TileMapsLayers> tileMapLayersList,
        TileBase warFogMapTileBase,
        GameObject tileMapTreasurePrefab,
        TileMapTreasure treasureConfig,
        TileMapHeal healConfig,
        List<TileBase> explodeFrames,
        TileBase borderTile,
        IStatusDisplay statusDisplay,
        IMessageBus messageBus,
        IHasEnemiesParams enemiesConfig,
        GameObject nuclearEffectPrefab
        )
    {
        mapSize = mapSizeMax;
        _tMapDefaultPrefab = tileMapPrefab;
        _tileMapLayersList = tileMapLayersList;
        _warFogMapTileBase = warFogMapTileBase;
        _tMapTreasurePrefab = tileMapTreasurePrefab;
        _treasureConfig = treasureConfig;
        _healConfig = healConfig;
        _explodeFrames = explodeFrames;
        _borderTile = borderTile;
        _statusDisplay = statusDisplay;
        _messageBus = messageBus;
        _camera = Camera.main;

        _nuclearEffectPrefab = nuclearEffectPrefab;

        _enemiesConfig = enemiesConfig;

        _lastSortingLayer = 0;
        _mainGrid = GetComponent<Grid>();

        // Собираем единый сид, который потом можно будет использовать для одинаковых карт
        // Например для сетевой игры, но это все потом... все потом...
        // TODO: Решить проблему с сидом сокровищ. Проблема: СИДА ДЛЯ СОКРОВИЩ НЕТ!!!
        //seed = CreateSeed();
        seed = 1010101;

        // Собираем слои
        BuildDefaultLayers();
        // В самом верху будут сокровища
        BuildTreasureLayer();
        BuildHealLayer();
        BuildEffectLayer();
        FillWarFog();
        BuildBorderLines();
    }

    private void Update()
    {
        _messageBus.Publish<EnemyClockMsg>();
    }

    public void OpenFogOfWarPart(Vector3Int center, int radius)
    {
        for (var x = Math.Max(0, center.x - radius); x <= Math.Min(mapSize.x - 1, center.x + radius); x++)
        for (var y = Math.Max(0, center.y - radius); y <= Math.Min(mapSize.y - 1, center.y + radius); y++)
        {
            _warFogTileMap.SetTile(new Vector3Int(x, y), null);
        }
    }
    public void OpenFogOfWarParts(HashSet<Vector3Int> positions)
    {
        HashSet<Vector3Int> except = new HashSet<Vector3Int>(positions);
        except.ExceptWith(_hashSetOfFog);
        Vector3Int[] arr = new Vector3Int[except.Count];
        except.CopyTo(arr);
        _warFogTileMap.SetTiles(arr,new TileBase[positions.Count]);
    }

    /// <summary>
    /// Проверяет пустая ли клетка, а клетка будет пустой полностью, если на ней нет песка.
    /// Поэтому метод ищет в самом первом хэш листе, потому что в первом листе должен лежать песок.
    /// </summary>
    /// <param name="position">Позиция блока для проверки</param>
    /// <returns>Если пусто(ноль блоков, нет ничего, пусто...), то TRUE</returns>
    public bool IsTileEmpty(Vector3Int position) => _destroyedHashList.First().Contains(position);

    public List<Vector3Int> GetFogCellsForEnemiesSpawn(Vector3Int center)
    {
        var retList = new List<Vector3Int>();

        var minRange = _enemiesConfig.NearSpawnDistance;
        var maxRange = _enemiesConfig.FarSpawnDistance;

        var leftDownCorner = new Vector2Int()
        {
            x = Math.Max(0, center.x - minRange),
            y = Math.Max(0, center.y - minRange)
        };

        var rightUpCorner = new Vector2Int()
        {
            x = Math.Min(mapSize.x - 1, center.x + minRange),
            y = Math.Max(mapSize.y - 1, center.y + minRange)
        };

        for (var x = leftDownCorner.x; x <= rightUpCorner.x; x++)
        for (var y = leftDownCorner.y; y <= rightUpCorner.y; y++)
        {

            var distFromCenter = Vector3.Distance(new Vector2(center.x, center.y), new Vector2(x, y));

            if ((distFromCenter < minRange) || (distFromCenter > maxRange))
                continue;

            var testPoint = new Vector3Int(x, y);

            if (!_warFogTileMap.HasTile(testPoint))
                continue;

            retList.Add(testPoint);
        }

        return retList;
    }

    private void FillWarFog()
    {
        var go = Instantiate(_tMapDefaultPrefab, transform);
        var renderer = go.GetComponent<TilemapRenderer>();
        go.GetComponent<TilemapCollider2D>().enabled = false;

        renderer.sortingOrder = _lastSortingLayer + 1;

        _lastSortingLayer += 2;

        _warFogTileMap = go.GetComponent<Tilemap>();

        if (!_warFogTileMap)
            Debug.LogError("Fog of war tilemap component was not found!");
        
        _warFogTileMap.FloodFill(mapSize,_warFogMapTileBase);
    }

    public void ClearLandingZone(Vector3Int center)
    {
        // Проходимся по всем добываемым картам
        // Удаляем по 2 блока со всех сторон квадратом
        HashSet<Vector3Int> destroyed = new HashSet<Vector3Int>();
        for (var x = center.x - 2; x <= center.x + 2; x++)
        for (var y = center.y - 2; y <= center.y + 2; y++)
            destroyed.Add(new Vector3Int(x, y));
        SetTilesDamage(destroyed,3);
        
        // Отдельно удаляем сокровища по 3 блока квардратом
        for (var x = center.x - 3; x <= center.x + 3; x++)
        for (var y = center.y - 3; y <= center.y + 3; y++)
            _treasureTileMapLayer.SetTile(new Vector3Int(x, y), null);
    }

    public void SetTileDamage(Vector3Int position, int damage)
    {
        for (int i = _destroyedHashList.Count - 1; i >= 0; i--)
        {
            if(_destroyedHashList[i].Contains(position)) continue;
            if(damage <= 0) break;
            damage--;
            _destroyedHashList[i].Add(position);
            var newPosZ = new Vector3Int(position.x, position.y, i);
            _destroyableTilemap.SetTile(newPosZ, null);
        }
    }
    public void SetTilesDamage(HashSet<Vector3Int> position, int damage)
    {
        Vector3Int[] array = new Vector3Int[position.Count * damage];
        for (int i = _destroyedHashList.Count - 1 ; i >= 0; i--)
        {
            // TODO: Не работает урон ментше 3
            if(damage <= 0) break;
            damage--;
            HashSet<Vector3Int> except = new HashSet<Vector3Int>();
            foreach (var pos in position)
            {
                except.Add(new Vector3Int(pos.x, pos.y, i));
            }
            except.ExceptWith(_destroyedHashList[i]);
            
            except.CopyTo(array,except.Count * damage);
            
            _destroyedHashList[i].UnionWith(except);
        }
        _destroyableTilemap.SetTiles(array,new TileBase[position.Count*_destroyedHashList.Count]);
    }

    private void BuildEffectLayer()
    {
        var go = Instantiate(_tMapDefaultPrefab, transform);
        var renderer = go.GetComponent<TilemapRenderer>();
        go.GetComponent<TilemapCollider2D>().enabled = false;
        renderer.sortingOrder = _lastSortingLayer;
        _lastSortingLayer++;
        _explodeEffectLayer = go.GetComponent<Tilemap>();
    }

    private void BuildBorderLines()
    {
        var go = Instantiate(_tMapDefaultPrefab, transform);
        var renderer = go.GetComponent<TilemapRenderer>();
        go.GetComponent<TilemapCollider2D>().enabled = true;
        renderer.sortingOrder = _lastSortingLayer;
        _lastSortingLayer++;
        _borderLayer = go.GetComponent<Tilemap>();

        for (int x = 0; x <= mapSize.x; x++)
            _borderLayer.SetTile(new Vector3Int(x,0), _borderTile);
        for (int x = 0; x <= mapSize.x; x++)
            _borderLayer.SetTile(new Vector3Int(x,mapSize.y), _borderTile);
        for (int y = 0; y <= mapSize.y; y++)
            _borderLayer.SetTile(new Vector3Int(0,y), _borderTile);
        for (int y = 0; y <= mapSize.y; y++)
            _borderLayer.SetTile(new Vector3Int(mapSize.x,y), _borderTile);

    }

    private void BuildBackgroundLayer(TileMapsLayers backgorundLayer)
    {
        var mainGo = Instantiate(_tMapDefaultPrefab, transform);
        mainGo.name = "BackgroundTileMap";
        var tR = mainGo.GetComponent<TilemapRenderer>();
        tR.sortingOrder = 0;
        var builder = mainGo.GetComponent<TilemapBuilder>();
        builder.Init(seed,mapSize);
        builder.GenerateOneLayer(
            backgorundLayer.isFill,
            backgorundLayer.tiles, 
            0);
    }
    private void BuildDefaultLayers()
    {
        var mainGo = Instantiate(_tMapDefaultPrefab, transform);
        var builder = mainGo.GetComponent<TilemapBuilder>();
        builder.Init(seed, mapSize); // Инициализируем держателя основной мапы
        mainGo.name = "DestructibleTileMap";
        mainGo.layer = LayerMask.NameToLayer("Rock");
        _destroyableTilemap = mainGo.GetComponent<Tilemap>();
        
        var tR = mainGo.GetComponent<TilemapRenderer>();
        _lastSortingLayer++;
        tR.sortingOrder = _lastSortingLayer;
        var layerCollider = mainGo.GetComponent<TilemapCollider2D>();
        if (!layerCollider)
            Debug.LogError("Component not found", layerCollider);
        layerCollider.enabled = true;
        
        for (int i = 0; i < _tileMapLayersList.Count; i++)
        {
            if (_tileMapLayersList[i].LayerType == GroundLayerType.Base)
            {
                BuildBackgroundLayer(_tileMapLayersList[i]);
                continue;
            }
            //Создаем на одной мапе слои из Z позиций
            //i-1 потому что нулевой i занял базовый слой
            var tiles = builder.GenerateOneLayer(
                _tileMapLayersList[i].isFill,
                _tileMapLayersList[i].tiles, 
                i-1);
            _destroyedHashList.Add(CreateHashSetFromTileBases(tiles));
            
        }
        
    }

    private HashSet<Vector3Int> CreateHashSetFromTileBases(TileBase[] tileBases)
    {
        var hashMap = new HashSet<Vector3Int>();
        int i = 0;
        for(int x = 0; x < mapSize.x; x++)
        for (int y = 0; y < mapSize.y; y++)
        {
            if (!tileBases[i]) hashMap.Add(new Vector3Int(x,y));
            i++;
        }
        return hashMap;
    }

    private void BuildTreasureLayer()
    {
        var go = Instantiate(_tMapTreasurePrefab, transform);
        var renderer = go.GetComponent<TilemapRenderer>();
        _treasureTileMapLayer = go.GetComponent<Tilemap>();
        renderer.sortingOrder = _lastSortingLayer;
        _lastSortingLayer++;
        var builder = go.GetComponent<TreasureBuilder>();
        builder.Init(
            mapSize,
            _treasureConfig.sectorSize,
            _treasureConfig.treasuresPerSector,
            _treasureConfig.treasureTiles
            );


    }
    private void BuildHealLayer()
    {
        var go = Instantiate(_tMapTreasurePrefab, transform);
        var tilemapRenderer = go.GetComponent<TilemapRenderer>();
        _healTileMapLayer = go.GetComponent<Tilemap>();
        tilemapRenderer.sortingOrder = _lastSortingLayer;
        _lastSortingLayer++;
        go.tag = "Heal";
        var builder = go.GetComponent<TreasureBuilder>();
        builder.Init(
            mapSize,
            _healConfig.sectorSize,
            _healConfig.healsPerSector,
            _healConfig.healTiles
        );


    }
    private int CreateSeed()
    {
        DateTime point = new DateTime(1970, 1, 1);
        TimeSpan time = DateTime.Now.Subtract(point);
        return (int)time.TotalSeconds;
    }

    /// <summary>
    /// Добыча камня и песка, сначала ломается камень по точке, потом по этой же точке ломается и песок
    /// </summary>
    /// <param name="worldPoint">Точка в мировом пространстве, внутри превращается в Vector3Int</param>
    /// <returns></returns>
    public void Earn(Vector3 worldPoint) => SetTileDamage(_mainGrid.WorldToCell(worldPoint),1);

    /// <summary>
    /// Добыча сокровищ
    /// </summary>
    /// <param name="worldPoint">Точка в мировом пространстве, внутри превращается в Vector3Int</param>
    /// <returns></returns>
    public void EarnTreasure(Vector3 worldPoint)
    {
        var localTilePoint = _mainGrid.WorldToCell(worldPoint);
        if (_treasureTileMapLayer.HasTile(localTilePoint))
            _treasureTileMapLayer.SetTile(localTilePoint, null);


    }
    /// <summary>
    /// Добыча HP
    /// </summary>
    /// <param name="worldPoint">Точка в мировом пространстве, внутри превращается в Vector3Int</param>
    /// <returns></returns>
    public void EarnHeal(Vector3 worldPoint)
    {
        var localTilePoint = _mainGrid.WorldToCell(worldPoint);

        if (_healTileMapLayer.HasTile(localTilePoint))
            _healTileMapLayer.SetTile(localTilePoint, null);


    }

    /// <summary>
    /// Устанавливает спрайт на тайлмапу
    /// </summary>
    /// <param name="sprite">Спрайт с PixelPerUnit равный остальным тайлам на мапе</param>
    /// <param name="worldPoint">Мировая координата</param>
    public void SetSpriteToMap(Sprite sprite, Vector3 worldPoint)
    {
        var pos = _mainGrid.WorldToCell(worldPoint);
        Tile tilebase = ScriptableObject.CreateInstance<Tile>();
        tilebase.sprite = sprite;
        _explodeEffectLayer.SetTile(pos,tilebase);
    }

    /// <summary>
    /// Получение координат на тайлмапе из мировых
    /// </summary>
    /// <param name="worldPoint">Мировые координаты</param>
    /// <returns></returns>
    public Vector3Int GetGridPosition(Vector3 worldPoint)
    {
        return _mainGrid.WorldToCell(worldPoint);
    }

    /// <summary>
    /// Получить стоимость сокровища по мировой координате
    /// </summary>
    /// <param name="worldPoint"></param>
    /// <returns>Возвращает стоимость сокровища, если не найдет вернет 0</returns>
    public int GetTreasureCost(Vector3 worldPoint)
    {
        return _treasureTileMapLayer.HasTile(_mainGrid.WorldToCell(worldPoint)) ?
            _treasureConfig.treasureTiles.Find(x =>
                x.tile == _treasureTileMapLayer.GetTile(_mainGrid.WorldToCell(worldPoint))).reward : 0;
    }
    //TODO: Объеденить одинаковые блоки кода
    public int GetHealCost(Vector3 worldPoint)
    {
        return _healTileMapLayer.HasTile(_mainGrid.WorldToCell(worldPoint)) ?
            _healConfig.healTiles.Find(x =>
                x.tile == _healTileMapLayer.GetTile(_mainGrid.WorldToCell(worldPoint))).reward : 0;
    }

    private IEnumerator WaitForExplode(Vector3 wordPos, int time)
    {
        _statusDisplay.CreateBombStatus(wordPos,time);
        yield return new WaitForSeconds(time);
    }

    /// <summary>
    /// Крестовой взрыв
    /// </summary>
    /// <param name="type">Тип крестового взрыва</param>
    /// <param name="radiusOfExplode">Радиус взрыва</param>
    /// <param name="worldPoint">Мировая координата</param>
    /// <param name="time">Ожидание взрыва</param>
    /// <returns></returns>
    public IEnumerator ExplodeCrossWorldPoint(int radiusOfExplode, LineBoomType type, Vector3 worldPoint, int time)
    {
        var localTilePoint = _mainGrid.WorldToCell(worldPoint);
        yield return WaitForExplode(localTilePoint, time);

        var info = new ExplodeInfo
        {
            Cell = new Vector2Int(localTilePoint.x, localTilePoint.y),
            Radius = radiusOfExplode
        };
        StartCoroutine(DetonatorRoutineCross(info,type));
    }

    /// <summary>
    /// Обычный круглый взрыв
    /// </summary>
    /// <param name="radiusOfExplode">Радиус взрыва</param>
    /// <param name="worldPoint">Мировая координата</param>
    /// <param name="time">Ожидание взрыва</param>
    /// <returns></returns>
    public IEnumerator ExplodeWorldPoint(int radiusOfExplode, Vector3 worldPoint, int time)
    {
        var localTilePoint = _mainGrid.WorldToCell(worldPoint);
        yield return WaitForExplode(localTilePoint, time);

        var info = new ExplodeInfo
        {
            Cell = new Vector2Int(localTilePoint.x, localTilePoint.y),
            Radius = radiusOfExplode
        };
        StartCoroutine(DetonatorRoutine(info));
    }

    private IEnumerator DetonatorRoutine(ExplodeInfo info)
    {
        var explosion = new ExplodePoint( this, _warFogTileMap,_explodeEffectLayer, _explodeFrames, this, _messageBus, _nuclearEffectPrefab);
        StartCoroutine(explosion.BoomRoutine(info.Cell.x, info.Cell.y, info.Radius, 1));
        yield return null;

    }
    private IEnumerator DetonatorRoutineCross(ExplodeInfo info, LineBoomType boomType)
    {
        var explosion = new ExplodePoint( this, _warFogTileMap, _explodeEffectLayer, _explodeFrames, this, _messageBus, _nuclearEffectPrefab);
        StartCoroutine(explosion.LineBoomRoutine(info.Cell.x, info.Cell.y, info.Radius, 1, boomType));
        yield return null;

    }

    public void DestroyTileMap()
    {
        Destroy(gameObject);
    }


}
[Serializable]
public struct TileMapsLayers
{
    public List<TilemapBuilder.TileMapLevel> tiles;
    public bool isFill;
    public GroundLayerType LayerType;
    private Vector2Int mapSize;
    private int seed;
}
[Serializable]
public struct TileMapTreasure
{
    public List<TreasureBuilder.ChancePerTile> treasureTiles;
    public int sectorSize;
    public int treasuresPerSector;
}
[Serializable]
public struct TileMapHeal
{
    public List<TreasureBuilder.ChancePerTile> healTiles;
    public int sectorSize;
    public int healsPerSector;
}
[Serializable]
public class ExplodeInfo
{
    public Vector2Int Cell;
    public int Radius;
    public float Delay;
}
