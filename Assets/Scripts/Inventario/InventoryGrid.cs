using System.Collections.Generic;

/// <summary>Una pila de objetos colocada en una cuadrícula. (x, y) = esquina de arriba a la izquierda de su caja.</summary>
public class ItemStack
{
    public ItemData item;
    public int count;
    public int x, y;
    public bool rotated;      // girado 90° en sentido antihorario (igual que el icono en la UI)

    public int W => rotated ? item.height : item.width;
    public int H => rotated ? item.width : item.height;
    public int SpaceLeft => item.maxStack - count;
    public bool CanRotate => item.width != item.height || !IsSymmetric90();

    public ItemStack(ItemData item, int count) { this.item = item; this.count = count; }

    /// <summary>¿Ocupa la casilla local (lx, ly) de su caja, con el giro actual?</summary>
    public bool Covers(int lx, int ly) => Covers(lx, ly, rotated);

    public bool Covers(int lx, int ly, bool rot)
    {
        if (!rot) return item.Occupies(lx, ly);
        // Giro antihorario: casilla girada (lx, ly) <- casilla original (width-1-ly, lx)
        return item.Occupies(item.width - 1 - ly, lx);
    }

    bool IsSymmetric90()
    {
        for (int y = 0; y < item.height; y++)
            for (int x = 0; x < item.width; x++)
                if (Covers(x, y, false) != Covers(x, y, true)) return false;
        return true;
    }
}

/// <summary>
/// Cuadrícula de inventario (lógica pura, sin UI). Casillas: x hacia la derecha, y hacia abajo.
/// Los objetos pueden tener cualquier forma (ItemData.Occupies); los huecos de su caja quedan libres.
/// </summary>
public class InventoryGrid
{
    public readonly int width, height;
    public readonly List<ItemStack> stacks = new List<ItemStack>();

    public InventoryGrid(int width, int height) { this.width = width; this.height = height; }

    public bool IsEmpty => stacks.Count == 0;

    bool CellInGrid(int cx, int cy) => cx >= 0 && cy >= 0 && cx < width && cy < height;

    /// <summary>Pila que ocupa de verdad la casilla (cx, cy), o null.</summary>
    public ItemStack At(int cx, int cy)
    {
        foreach (var s in stacks)
        {
            int lx = cx - s.x, ly = cy - s.y;
            if (lx >= 0 && ly >= 0 && lx < s.W && ly < s.H && s.Covers(lx, ly)) return s;
        }
        return null;
    }

    /// <summary>¿Caben todas las casillas de 's' dentro de la cuadrícula en (x, y) con ese giro?</summary>
    public bool InBounds(ItemStack s, int x, int y, bool rot)
    {
        int w = rot ? s.item.height : s.item.width, h = rot ? s.item.width : s.item.height;
        for (int ly = 0; ly < h; ly++)
            for (int lx = 0; lx < w; lx++)
                if (s.Covers(lx, ly, rot) && !CellInGrid(x + lx, y + ly)) return false;
        return true;
    }

    /// <summary>Pilas (distintas de 's') que chocan con las casillas de 's' puesta en (x, y).</summary>
    public List<ItemStack> Overlapping(ItemStack s, int x, int y, bool rot)
    {
        var list = new List<ItemStack>();
        int w = rot ? s.item.height : s.item.width, h = rot ? s.item.width : s.item.height;
        for (int ly = 0; ly < h; ly++)
            for (int lx = 0; lx < w; lx++)
            {
                if (!s.Covers(lx, ly, rot)) continue;
                var o = At(x + lx, y + ly);
                if (o != null && o != s && !list.Contains(o)) list.Add(o);
            }
        return list;
    }

    public bool IsFree(ItemStack s, int x, int y, bool rot) => InBounds(s, x, y, rot) && Overlapping(s, x, y, rot).Count == 0;

    /// <summary>Coloca la pila en (x, y) con ese giro si hay sitio.</summary>
    public bool Place(ItemStack s, int x, int y, bool rot)
    {
        if (!IsFree(s, x, y, rot)) return false;
        s.x = x; s.y = y; s.rotated = rot;
        stacks.Add(s);
        return true;
    }

    public void Remove(ItemStack s) => stacks.Remove(s);

    /// <summary>Busca el primer hueco (de arriba a la izquierda), probando sin girar y girado.</summary>
    public bool TryAutoPlace(ItemStack s)
    {
        for (int y = -s.item.width; y < height; y++)          // empieza fuera: las formas con huecos
            for (int x = -s.item.width; x < width; x++)       // pueden encajar con su caja asomando
            {
                if (Place(s, x, y, false)) return true;
                if (s.CanRotate && Place(s, x, y, true)) return true;
            }
        return false;
    }

    /// <summary>Rellena las pilas de ese objeto que no estén llenas. Devuelve lo que sobra.</summary>
    public int TopUp(ItemData item, int count)
    {
        foreach (var s in stacks)
        {
            if (count <= 0) break;
            if (s.item != item || s.SpaceLeft <= 0) continue;
            int n = System.Math.Min(count, s.SpaceLeft);
            s.count += n; count -= n;
        }
        return count;
    }

    /// <summary>Crea pilas nuevas (divididas por maxStack) en huecos libres. Devuelve lo que no cabe.</summary>
    public int AddNewStacks(ItemData item, int count)
    {
        while (count > 0)
        {
            var s = new ItemStack(item, System.Math.Min(count, item.maxStack));
            if (!TryAutoPlace(s)) break;
            count -= s.count;
        }
        return count;
    }
}
