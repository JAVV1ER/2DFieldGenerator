using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

//[ExecuteInEditMode]
[RequireComponent(typeof(Tilemap))]
public class TilemapBuilder : MonoBehaviour
{
    private Vector3Int _mapSize = new(100,100);
    private int _seed = 100;
    
    private float _scale = 22.82f;
    private int _octaves = 6;
    
    private Vector2 _offset;
    private float _frequency = 2.5f;
    private float _amplitude = 16.6f;
    private Tilemap _tilemap;
    private bool _isInited = false;
    
    [Serializable] public struct TileMapLevel
    {
        public TileBase tile;
        public float height;
    }
    
    public void Init(int seed, Vector3Int mapSize)
    {
        _seed = seed;
        _mapSize = mapSize;
        _tilemap = GetComponent<Tilemap>();
        // Чистим на всякий случай от лишнего мусора
        _tilemap.ClearAllTiles();
        _isInited = true;
    }

    
    public TileBase[] GenerateOneLayer(bool isFill, List<TileMapLevel> tileLevels, int zOffset)
    {
        if(!_isInited) 
            Debug.LogError("Builder is not inited !!!", this);
        
        var coordsToSet = new Vector3Int[(_mapSize.x * _mapSize.y)];
        var filledField = new TileBase[(_mapSize.x * _mapSize.y)];
        var i = 0;
        for(int x = 0; x < _mapSize.x; x++)
        {
            for (int y = 0; y < _mapSize.y; y++)
            {
                coordsToSet.SetValue(new Vector3Int(x, y, zOffset),i);
                i++;
            }
        }
        if (isFill)
        {
            Array.Fill(filledField,tileLevels.FirstOrDefault().tile);
            
            SetTileArray(coordsToSet,filledField);
            return filledField;
        }
        var noise = NoiseMapGenerator.GenerateNoiseMap(_mapSize.y, _mapSize.x, _seed, _scale, _octaves, _offset, _frequency,_amplitude);
        filledField = TileSwitcher(noise, tileLevels);
        SetTileArray(coordsToSet,filledField);
        return filledField;
    }
    
    /// <summary>
    /// Берет (массив шума) и лист (тайл + высота шума)
    /// Сопостовляет их и возвращает массив тайлов.
    /// </summary>
    /// <param name="noise">Массив шума (должен быть размером X*Y размера карты, тоесть вмещать в себя 10*10 = 100 элементов)</param>
    /// <param name="tilemaplevels">Лист структуры ТАЙЛ + ВЫСОТА, ДАННЫЙ тайл будет находится на ДАННОЙ высоте(по крайней мере должен)</param>
    /// <returns></returns>
    TileBase[] TileSwitcher(float[] noise, List<TileMapLevel> tilemaplevels)
    {
        TileBase[] tiles = new TileBase[noise.Length];
        for (int i = 0; i < noise.Length; i++)
            tiles[i] = tilemaplevels.FindLast(h => h.height < noise[i]).tile;

        return tiles;
    }
    void SetTileArray(Vector3Int[] vector3Ints, TileBase[] tbases)
    {
        _tilemap.SetTiles(vector3Ints, tbases);
    }
}
