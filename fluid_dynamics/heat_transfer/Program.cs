using System.IO.Compression;
using System.Security.AccessControl;

namespace program;

public class Mesh {
    class Cell(Border? up = null, Border? right = null, Border? down = null, Border? left = null)
    {
        public Border? up;
        public Border? right; 
        public Border? down;
        public Border? left;
        public double t;
    }

    class Border
    {
        /// <summary>
        /// Реализация границы.
        /// (От past к next) past -> next: слево -> направо, сверху -> вниз.
        /// </summary>
        
        public Cell? past, next; // слево -> направо, сверху -> вниз
        public bool end;
    }

    uint length {get;}
    uint width {get;}
    List<Cell> cellList {get;}
    List<Border> borderXList {get;}
    List<Border> borderYList {get;}

    Mesh(uint l, uint w) 
    {
        if (l == 0 || w == 0) 
        {
            throw new ArgumentNullException();
        }

        length = l;
        width = w;

        for (uint i = 0; i <= l; i ++)
        {
            for (uint j = 0; j <= w; j ++)
            {
                borderXList.Add(new Border());
                borderYList.Add(new Border());
                cellList.Add(new Cell());
                if
            }
        }
    }
}

class Program {
    void Main() {

    }
}