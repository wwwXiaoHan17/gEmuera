using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.Modern.Script;

internal sealed class SparseArray<T>
{
    private readonly T _defaultValue;
    private Dictionary<long, T> _data;
    private int _logicalLength;

    public SparseArray(T defaultValue = default)
    {
        _defaultValue = defaultValue;
        _data = new Dictionary<long, T>();
        _logicalLength = 0;
    }

    public int Length
    {
        get => _logicalLength;
        set => _logicalLength = value;
    }

    public T this[long index]
    {
        get
        {
            if (_data.TryGetValue(index, out var val))
                return val;
            return _defaultValue;
        }
        set
        {
            if (EqualityComparer<T>.Default.Equals(value, _defaultValue))
                _data.Remove(index);
            else
                _data[index] = value;
        }
    }

    public void Clear() => _data.Clear();

    public int Count => _data.Count;

    public IEnumerable<KeyValuePair<long, T>> Entries => _data;

    public bool ContainsIndex(long index) => _data.ContainsKey(index);

    public void Remove(long index) => _data.Remove(index);

    public long MemorySavedBytes
    {
        get
        {
            int size = typeof(T) == typeof(long) ? 8 :
                       typeof(T) == typeof(double) ? 8 :
                       typeof(T) == typeof(string) ? IntPtr.Size : IntPtr.Size;
            return (_logicalLength - _data.Count) * size;
        }
    }

    public void Shift(int offset, T defaultValue, int start, int count)
    {
        if (offset == 0) return;
        var newDict = new Dictionary<long, T>();
        int end = start + count;

        foreach (var kvp in _data)
        {
            long k = kvp.Key;
            if (k < start || k >= end)
            {
                newDict[k] = kvp.Value;
                continue;
            }

            long newKey = k + offset;
            if (newKey >= start && newKey < end && newKey < _logicalLength)
                newDict[newKey] = kvp.Value;
        }

        if (!EqualityComparer<T>.Default.Equals(defaultValue, _defaultValue))
        {
            if (offset > 0)
            {
                for (long i = start; i < start + offset && i < end; i++)
                    newDict[i] = defaultValue;
            }
            else
            {
                for (long i = end + offset; i < end && i >= start; i++)
                    newDict[i] = defaultValue;
            }
        }

        _data = newDict;
    }

    public void Sort(bool ascending, int start, int count)
    {
        int end = Math.Min(start + count, _logicalLength);
        var temp = new T[end - start];
        for (int i = 0; i < temp.Length; i++)
            temp[i] = this[start + i];

        Array.Sort(temp);
        if (!ascending) Array.Reverse(temp);

        for (int i = 0; i < temp.Length; i++)
            this[start + i] = temp[i];
    }

    public void RemoveRange(int start, int count)
    {
        int end = Math.Min(start + count, _logicalLength);
        var newDict = new Dictionary<long, T>();

        foreach (var kvp in _data)
        {
            long k = kvp.Key;
            if (k < start)
            {
                newDict[k] = kvp.Value;
            }
            else if (k >= end)
            {
                long newKey = k - count;
                if (newKey >= 0 && newKey < _logicalLength)
                    newDict[newKey] = kvp.Value;
            }
        }

        _data = newDict;
    }

    public T[] ToArray(int length)
    {
        var result = new T[length];
        for (int i = 0; i < length; i++)
        {
            if (_data.TryGetValue(i, out var val))
                result[i] = val;
            else
                result[i] = _defaultValue;
        }
        return result;
    }

    public void FromArray(T[] source)
    {
        _data.Clear();
        for (int i = 0; i < source.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(source[i], _defaultValue))
                _data[i] = source[i];
        }
    }
}

internal sealed class SparseArray2D<T>
{
    private readonly T _defaultValue;
    private readonly Dictionary<(long, long), T> _data;
    private int _length1;
    private int _length2;

    public SparseArray2D(T defaultValue = default)
    {
        _defaultValue = defaultValue;
        _data = new Dictionary<(long, long), T>();
    }

    public (int Len1, int Len2) LogicalSize
    {
        get => (_length1, _length2);
        set => (_length1, _length2) = value;
    }

    public T this[long i, long j]
    {
        get
        {
            if (_data.TryGetValue((i, j), out var val))
                return val;
            return _defaultValue;
        }
        set
        {
            if (EqualityComparer<T>.Default.Equals(value, _defaultValue))
                _data.Remove((i, j));
            else
                _data[(i, j)] = value;
        }
    }

    public void Clear() => _data.Clear();

    public int Count => _data.Count;

    public IEnumerable<KeyValuePair<(long, long), T>> Entries => _data;

    public bool ContainsIndex(long i, long j) => _data.ContainsKey((i, j));

    public void Remove(long i, long j) => _data.Remove((i, j));

    public long MemorySavedBytes
    {
        get
        {
            int size = typeof(T) == typeof(long) ? 8 :
                       typeof(T) == typeof(double) ? 8 :
                       typeof(T) == typeof(string) ? IntPtr.Size : IntPtr.Size;
            long totalSlots = (long)_length1 * _length2;
            return (totalSlots - _data.Count) * size;
        }
    }

    public T[,] ToArray2D(int len1, int len2)
    {
        var result = new T[len1, len2];
        for (int i = 0; i < len1; i++)
            for (int j = 0; j < len2; j++)
            {
                if (_data.TryGetValue((i, j), out var val))
                    result[i, j] = val;
                else
                    result[i, j] = _defaultValue;
            }
        return result;
    }

    public void FromArray2D(T[,] source)
    {
        _data.Clear();
        int len1 = source.GetLength(0);
        int len2 = source.GetLength(1);
        for (int i = 0; i < len1; i++)
            for (int j = 0; j < len2; j++)
            {
                if (!EqualityComparer<T>.Default.Equals(source[i, j], _defaultValue))
                    _data[(i, j)] = source[i, j];
            }
    }
}
