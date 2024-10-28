using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;
using Random = UnityEngine.Random;

[ExecuteInEditMode]
public class TreasureBuilder : MonoBehaviour
{
    [Tooltip("Размер карты в пикселях (лучше указывать кратные 10)")]
    private Vector3Int _size;
    [Tooltip("Размер одного сектора (количество секторов вычисляется из размера карты/размер сектора)")]
    private int _sectorSize;
    [Tooltip("Количество сокровищ в одном секторе")]
    private int _countTreasures;
    [Tooltip("Tile сокровища")]
    private List<ChancePerTile> _treasureList;
    
    private Tilemap _tilemap;
    List<string> TileNames = new List<string>();
    [Serializable] public struct ChancePerTile
    {
        public TileBase tile;
        public float chance;
        public int reward;
    }
    

    public void Init(Vector3Int size, int sectorSize, int countPerSector, List<ChancePerTile> tileList)
    {
        _size = size;
        _sectorSize = sectorSize;
        _countTreasures = countPerSector;
        _treasureList = tileList;
        _tilemap = GetComponent<Tilemap>();
        Generate();
    }
    

    public void Generate()
    {
        _tilemap.ClearAllTiles();
        
        for (int x = 0; x < _size.x / _sectorSize; x++)
            for (int y = 0; y < _size.y / _sectorSize; y++)
            {
                var count = 0;
                for (int i = 0; i < _countTreasures; i++)
                {
                    var xPos = ((_sectorSize) * x) + Random.value * (_sectorSize - 1);
                    var yPos = ((_sectorSize) * y) + Random.value * (_sectorSize - 1);
                    var position = Vector3Int.CeilToInt(new Vector2(xPos, yPos));
                    var chance = Random.value;
                    TileBase luckyTile = null;
                    foreach (var treasure in _treasureList)
                    {
                        if (treasure.chance <= chance)
                        {
                            luckyTile = treasure.tile;
                            
                        }
                    }

                    if (!luckyTile) luckyTile = _treasureList.Last().tile;
                    TileNames.Add(luckyTile.name);
                    _tilemap.SetTile(position, luckyTile);
                    count++;
                }
                Debug.LogWarning("tiles per sector: " + count);
            }
        
        Debug.LogWarning("tiles per sector: " + TileNames.Count);
        //TestCount();
    }

    void TestCount()
    {
        var bones = 0;
        var rare = 0;
        var myt = 0;
        var leg = 0;
        TileNames.Sort();
        foreach (var name in TileNames)
        {
            if (name == "Bones") bones++;
            if (name == "RareTreasure") rare++;
            if (name == "MythTreasure") myt++;
            if (name == "LegendTreasure") leg++;
        }
        
    }
}
