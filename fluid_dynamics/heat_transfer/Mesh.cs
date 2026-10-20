using System.ComponentModel;
using System.IO.Compression;
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
    NArray<Face> _faceXList, _faceYList, _faceZList;

    public int[] Shape()
    {
        int[] copy = new int[_shape.Length];
        System.Array.Copy(_shape, copy, _shape.Length);
        return copy;
    }
    public NArray<Cell> CellList()
    {
        return _cellList.Copy();
    }
    public NArray<Face> FaceXList()
    {
        return _faceXList.Copy();
    }
    public NArray<Face> FaceYList()
    {
        return _faceYList.Copy();
    }
    public NArray<Face> FaceZList()
    {
        return _faceZList.Copy();
    }

    public Mesh(int X, int Y, int Z) 
    {
        if (X <= 0 || Y <= 0 || Z <= 0) 
        {
            throw new ArgumentOutOfRangeException("X or Y or Z <= 0");
        }

        
    }
}