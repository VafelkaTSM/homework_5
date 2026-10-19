using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;

namespace program;

public class Cell(Border? up = null, Border? right = null, Border? down = null, Border? left = null)
{
    public Border? up;
    public Border? right; 
    public Border? down;
    public Border? left;
    public double t;
}

public class Border
{
    /// <summary>
    /// Реализация границы.
    /// (От past к next) past -> next: слево -> направо, сверху -> вниз.
    /// </summary>
    
    public Cell? past, next; // слево -> направо, сверху -> вниз
    public bool end;
}

public class NArray<T>: IEnumerable<T>
{
    private int[] _shape;
    private int[] _cumlength;
    private T[] _array;

    public int[] Shape()
    {
        int[] copy = new int[_shape.Length];
        System.Array.Copy(_shape, copy, _shape.Length);
        return copy;
    }
    public int[] Cumlength()
    {
        int[] copy = new int[_cumlength.Length];
        System.Array.Copy(_cumlength, copy, _shape.Length);
        return copy;
    }
    public T[] Array()
    {
        T[] newArray = new T[_array.Length];
        System.Array.Copy(_array, newArray, _array.Length);
        return newArray;
    }

    public NArray(int[] shape)
    {
        if (shape == null || shape.Length == 0) {
            throw new ArgumentOutOfRangeException("Shape is null or zero length shape.");
        }
        for (int i = 0; i < shape.Length - 1; i ++)
        {
            if (shape[i] < 1)
            {
                throw new ArgumentOutOfRangeException($"{shape[i]} < 1 in shape.");
            }
        }

        int length = 1;
        for (int i = 0; i < shape.Length - 1; i ++)
        {
            length *= shape[i];
        }
        _shape = shape;
        _cumlength = new int[length];
        _array = new T[length];
        _cumlength[_shape.Length - 1] = 1;
        for (int i = _shape.Length - 1; i >= 1; i--)
        {
            _cumlength[i - 1] = length;
        }
    }

    public NArray(int[] shape, T[] rowArray)
    {
        if (shape == null || shape.Length == 0) {
            throw new ArgumentOutOfRangeException("Shape is null or zero length shape.");
        }
        int length = 1;
        for (int i = 0; i < shape.Length - 1; i ++)
        {
            if (shape[i] < 1)
            {
                throw new ArgumentOutOfRangeException($"{shape[i]} < 1 in shape.");
            }
            length *= shape[i];
        }
        if (length != rowArray.Length)
        {
            throw new ArgumentOutOfRangeException($"rowArray doesn't fit in terms of shape.");
        }

        _shape = shape;
        _array = rowArray;
        _cumlength = new int[length];
        for (int i = _shape.Length - 1; i >= 1; i--)
        {
            _cumlength[i - 1] = length;
        }
    }

    public NArray(NArray<T> narray)
    {
        _shape = narray.
    }

    public T this[params int[] indices]
    {
        get
        {
            if (indices.Length > _shape.Length)
            {
                throw new ArgumentOutOfRangeException($"number of indices too large.");
            }
            for (int i = 0; i < _shape.Length; i ++)
            {
                if (indices[i] > _shape[i])
                {
                    throw new ArgumentOutOfRangeException($"indices[i] == {indices[i]} out of range");
                }
            }
            int index = 0;
            for (int i = _shape.Length - 1; i >= 0; i --)
            {
                index += _shape[i] * _cumlength[i];
            }
            
            return _array[index];
        }

        set
        {
            if (indices.Length > _shape.Length)
            {
                throw new ArgumentOutOfRangeException($"number of indices too large.");
            }
            for (int i = 0; i < _shape.Length; i ++)
            {
                if (indices[i] > _shape[i])
                {
                    throw new ArgumentOutOfRangeException($"indices[i] == {indices[i]} out of range");
                }
            }
            int index = 0;
            for (int i = _shape.Length - 1; i >= 0; i --)
            {
                index += _shape[i] * _cumlength[i];
            }

            _array[index] = value;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (_shape.Length > 1)
        {
            yield return NArray<T>
        }
    }
}

public class Mesh {

    public int High {get;}
    public int Width {get;}
    readonly Cell[] _cellList;
    readonly Border[] _borderYList, _borderXList;
    public Cell[] CellList => _cellList;
    public Border[] BorderYList => _borderYList;
    public Border[] BorderXList => _borderXList;

    public Mesh(int high, int width) 
    {
        if (l == 0 || w == 0) 
        {
            throw new ArgumentNullException();
        }

        _high = high;
        _width = width;
        cellList = new Cell[h * w];
        borderYList = new Border[(h + 1) * w];
        borderXList = new Border[h * (w + 1)];

        for (int i = 0; i <= h; i ++)
        {
            for (int j = 0; j <= w; j ++)
            {
                borderXList.Add(new Border());
                borderYList.Add(new Border());
                cellList.Add(new Cell());
                if (i != 0)
                {
                    borderXList[i + j * (w + 1)].past = cellList[i + j * w]
                }
            }
        }
    }


}

class Program {
    void Main() {

    }
}