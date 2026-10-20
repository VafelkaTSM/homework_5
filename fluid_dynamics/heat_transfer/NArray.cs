using System.Runtime.CompilerServices;

namespace NArray;

public class NArray<T>
{

    int[] _shape;
    int[] _cumlength;
    T[] _array;

    public int[] Shape()
    {
        int[] copy = new int[_shape.Length];
        System.Array.Copy(_shape, copy, _shape.Length);
        return copy;
    }
    public int[] Cumlength()
    {
        int[] copy = new int[_cumlength.Length];
        System.Array.Copy(_cumlength, copy, _cumlength.Length);
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
        for (int i = 0; i < shape.Length; i ++)
        {
            if (shape[i] < 1)
            {
                throw new ArgumentOutOfRangeException($"{shape[i]} < 1 in shape.");
            }
        }

        _shape = new int[shape.Length];
        System.Array.Copy(shape, _shape, shape.Length);

        int length = 1;
        for (int i = 0; i < _shape.Length; i ++)
        {
            length *= _shape[i];
        }

        _array = new T[length];
        _cumlength = new int[_shape.Length];
        int stride = 1;
        for (int i = _shape.Length - 1; i >= 0; i--)
        {
            _cumlength[i] = stride;
            stride *= _shape[i];
        }
    }

    public NArray(int[] shape, T[] rowArray)
    {
        if (shape == null || shape.Length == 0) {
            throw new ArgumentOutOfRangeException("Shape is null or zero length shape.");
        }
        if (rowArray == null)
        {
            throw new ArgumentOutOfRangeException("rowArray is null.");
        }
        int length = 1;
        for (int i = 0; i < shape.Length; i ++)
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

        _shape = new int[shape.Length];
        System.Array.Copy(shape, _shape, shape.Length);

        _array = new T[length];
        System.Array.Copy(rowArray, _array, length);

        _cumlength = new int[_shape.Length];
        int stride = 1;
        for (int i = _shape.Length - 1; i >= 0; i--)
        {
            _cumlength[i] = stride;
            stride *= _shape[i];
        }
    }

    public NArray(NArray<T> narray)
    {
        _shape = narray.Shape();
        _cumlength = narray.Cumlength();
        _array = narray.Array();
    }

    public NArray<T> Copy() => new NArray<T>(this);

    public T this[params int[] indices]
    {
        get
        {
            if (indices == null || indices.Length != _shape.Length)
            {
                throw new ArgumentOutOfRangeException("number of indices is not equal to the rank.");
            }

            int index = 0;
            for (int i = 0; i < _shape.Length; i ++)
            {
                if (indices[i] < 0 || indices[i] >= _shape[i])
                {
                    throw new ArgumentOutOfRangeException($"indices[{i}] == {indices[i]} out of range");
                }
                index += indices[i] * _cumlength[i];
            }
            
            return _array[index];
        }

        set
        {
            if (indices == null || indices.Length != _shape.Length)
            {
                throw new ArgumentOutOfRangeException("number of indices is not equal to the rank.");
            }
            
            int index = 0;
            for (int i = 0; i < _shape.Length; i ++)
            {
                if (indices[i] < 0 || indices[i] >= _shape[i])
                {
                    throw new ArgumentOutOfRangeException($"indices[{i}] == {indices[i]} out of range");
                }
                index += indices[i] * _cumlength[i];
            }

            _array[index] = value;
        }
    }
}