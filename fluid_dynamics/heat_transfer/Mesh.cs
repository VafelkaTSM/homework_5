using System.ComponentModel;
using System.IO.Compression;
using NArray;

public class Cell
{
    public Face[] Faces = new Face[6]; // {x_past, x_next, ..., z_past, z_next}
    public double Temp;

    public Cell(Face[]? faces = null, double temp = 0)
    {
        if (faces != null && faces.Length != Faces.Length)
        {
            throw new ArgumentOutOfRangeException($"faces is not null and don't has a length equal to {Faces.Length}");
        }

        if (faces != null)
        {
            System.Array.Copy(faces, Faces, Faces.Length);
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

    public Face(Cell[]? cells = null, bool end = false)
    {
        if (cells != null && cells.Length != 2)
        {
            throw new ArgumentOutOfRangeException($"cells is not null and don't has a length equal to {Cells.Length}");
        }

        if (cells != null)
        {
            System.Array.Copy(cells, Cells, Cells.Length);
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

        _shape = new int[] { X, Y, Z };
        _cellList = new NArray<Cell>(new int[] { X, Y, Z });
        _faceXList = new NArray<Face>(new int[] { X + 1, Y, Z });
        _faceYList = new NArray<Face>(new int[] { X, Y + 1, Z });
        _faceZList = new NArray<Face>(new int[] { X, Y, Z + 1 });

        for (int y = 0; y < Y; y++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceXList[0, y, z] = new Face(null, true);
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceYList[x, 0, z] = new Face(null, true);
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int y = 0; y < Y; y++)
            {
                _faceZList[x, y, 0] = new Face(null, true);
            }
        }


        for (int x = 0; x < X; x++)
        {
            for (int y = 0; y < Y; y++)
            {
                for (int z = 0; z < Z; z++)
                {
                    if (x != 0 && y != 0 && z != 0)
                    {
                        continue;
                    }

                    Face faceX = (x == 0) ? _faceXList[x, y, z] : CreateFaceX(x, y, z);
                    Face faceY = (y == 0) ? _faceYList[x, y, z] : CreateFaceY(x, y, z);
                    Face faceZ = (z == 0) ? _faceZList[x, y, z] : CreateFaceZ(x, y, z);

                    CreateCell(x, y, z, faceX, faceY, faceZ);
                }
            }
        }

        for (int x = 1; x < X; x++)
        {
            for (int y = 1; y < Y; y++)
            {
                for (int z = 1; z < Z; z++)
                {
                    CreateCell(x, y, z, CreateFaceX(x, y, z), CreateFaceY(x, y, z), CreateFaceZ(x, y, z));
                }
            }
        }

        for (int y = 0; y < Y; y++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceXList[X, y, z] = CreateFaceX(X, y, z, true);
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceYList[x, Y, z] = CreateFaceY(x, Y, z, true);
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int y = 0; y < Y; y++)
            {
                _faceZList[x, y, Z] = CreateFaceZ(x, y, Z, true);
            }
        }
    }

    private Face CreateFaceX(int x, int y, int z, bool end = false)
    {
        Face face = new Face(null, end);
        _faceXList[x, y, z] = face;
        face.Cells[0] = _cellList[x - 1, y, z];
        _cellList[x - 1, y, z].Faces[1] = face;
        return face;
    }

    private Face CreateFaceY(int x, int y, int z, bool end = false)
    {
        Face face = new Face(null, end);
        _faceYList[x, y, z] = face;
        face.Cells[0] = _cellList[x, y - 1, z];
        _cellList[x, y - 1, z].Faces[3] = face;
        return face;
    }

    private Face CreateFaceZ(int x, int y, int z, bool end = false)
    {
        Face face = new Face(null, end);
        _faceZList[x, y, z] = face;
        face.Cells[0] = _cellList[x, y, z - 1];
        _cellList[x, y, z - 1].Faces[5] = face;
        return face;
    }

    private Cell CreateCell(int x, int y, int z, Face faceX, Face faceY, Face faceZ)
    {
        Cell cell = new Cell();
        _cellList[x, y, z] = cell;
        faceX.Cells[1] = cell;
        faceY.Cells[1] = cell;
        faceZ.Cells[1] = cell;
        cell.Faces[0] = faceX;
        cell.Faces[2] = faceY;
        cell.Faces[4] = faceZ;
        return cell;
    }
}