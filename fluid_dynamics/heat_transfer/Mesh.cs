using System.ComponentModel;
using NArray;

public class Cell
{
    public Face[] Faces = new Face[6]; // {x_past, x_next, ..., z_past, z_next}
    public double Temp;

    public Cell(Face[] faces = null, double temp = 0)
    {
        if (faces != null && faces.Length != Faces.Length)
        {
            throw new ArgumentOutOfRangeException($"faces is not null and don't has a length equal to {Faces.Length}");
        }

        if (faces != null)
        {
            System.Array.Copy(Faces, faces, Faces.Length);
        }
        Temp = temp;
    }
}

public class Face
{
    /// <summary>
    /// Реализация границы.
    /// </summary>
    
    public Cell[] Cells = new Cell[2]; // {past, next}
    public bool End;

    public Face(Cell[] cells = null, bool end = false)
    {
        if (cells != null && cells.Length != 2)
        {
            throw new ArgumentOutOfRangeException($"cells is not null and don't has a length equal to {Cells.Length}");
        }

        if (cells != null)
        {
            System.Array.Copy(Cells, cells, Cells.Length);
        }
        End = end;
    }
}

public class Mesh {
    int[] _shape;
    NArray<Cell> _cellList;
    NArray<Face> _borderXList, _borderYList, _borderZList;

    public int[] Shape()
    {
        int[] copy = new int[_shape.Length];
        System.Array.Copy(_shape, copy, _shape.Length);
        return copy;
    }
    public int[] CellList()
    {
        int[] copy = new int[_cellList.Length];
        System.Array.Copy(_cellList, copy, _cellList.Length);
        return copy;
    }
    public int[] BorderXList()
    {
        int[] copy = new int[_borderXList.Length];
        System.Array.Copy(_borderXList, copy, _borderXList.Length);
        return copy;
    }
    public int[] BorderYList()
    {
        int[] copy = new int[_borderYList.Length];
        System.Array.Copy(_borderYList, copy, _borderYList.Length);
        return copy;
    }
    public int[] BorderZList()
    {
        int[] copy = new int[_borderZList.Length];
        System.Array.Copy(_borderZList, copy, _borderZList.Length);
        return copy;
    }

    public Mesh(int X, int Y, int Z) 
    {
        if (X == 0 || Y == 0 || Z == 0) 
        {
            throw new ArgumentOutOfRangeException("X or Y or Z <= 0");
        }
    }
}