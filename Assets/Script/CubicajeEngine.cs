using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resultado de colocación de una pieza dentro del pallet.
/// Equivalente dinámico al PieceDef hardcodeado en CubicajeARManager.
/// </summary>
[Serializable]
public class PlacedPiece
{
    public string id;          // Identificador de la pieza (ej: "A1")
    public Vector3 localPos;   // Centro en espacio local del pallet
    public Vector3 localEuler; // Rotación Euler en espacio local (para holograma y operador)
    public Vector3 scale;      // Dimensiones reales tras aplicar orientación (X=ancho, Y=alto, Z=fondo)
}

/// <summary>
/// Motor de cubicaje: calcula posición y orientación de cada pieza dentro del pallet.
///
/// Algoritmo: shelf packing greedy 3D.
///   1. Ordena piezas por volumen descendente (piezas más grandes primero).
///   2. Para cada pieza elige la orientación con menor altura (Y) que quepa.
///   3. Coloca de izquierda a derecha (X); cuando la fila se llena, avanza en profundidad (Z).
///   4. Cuando la capa se llena, sube a la siguiente capa (Y).
///   5. La secuencia de ensamblado se ordena: Y asc → Z asc → X asc
///      (piso primero, fondo antes que frente).
/// </summary>
public static class CubicajeEngine
{
    // Estructura interna para representar una orientación candidata de una pieza.
    private struct Orientation
    {
        public float w;        // Dimensión en X (ancho real tras rotar)
        public float h;        // Dimensión en Y (alto real tras rotar)
        public float d;        // Dimensión en Z (fondo real tras rotar)
        public Vector3 euler;  // Rotación Euler correspondiente (aproximada para el holograma)
    }

    /// <summary>
    /// Resuelve la colocación de todas las piezas dentro del pallet y devuelve
    /// la lista ordenada como secuencia de ensamblado.
    /// </summary>
    /// <param name="pieces">Lista de piezas a colocar (de piezas.json).</param>
    /// <param name="palletInner">Dimensiones interiores del pallet en metros (X=ancho, Y=alto, Z=fondo).</param>
    /// <param name="margin">Margen mínimo entre piezas y entre piezas y paredes (m).</param>
    /// <returns>Piezas colocadas, ordenadas por secuencia de ensamblado (Y↑ Z↑ X↑).</returns>
    public static List<PlacedPiece> Solve(List<PieceEntry> pieces, Vector3 palletInner, float margin = 0.002f)
    {
        if (pieces == null || pieces.Count == 0)
        {
            Debug.LogWarning("[CubicajeEngine] La lista de piezas está vacía.");
            return new List<PlacedPiece>();
        }

        // Ordenar por volumen descendente: piezas grandes primero aprovechan mejor el espacio
        var sorted = new List<PieceEntry>(pieces);
        sorted.Sort((a, b) => PieceVolume(b).CompareTo(PieceVolume(a)));

        var result = new List<PlacedPiece>();

        // Estado del cursor de colocación (en espacio local del pallet, centro = 0,0,0)
        float layerYMin  = -palletInner.y / 2f + margin; // borde inferior de la capa actual
        float layerMaxH  = 0f;                            // altura máxima vista en la capa actual
        float rowZMin    = -palletInner.z / 2f + margin;  // borde trasero de la fila actual
        float rowMaxD    = 0f;                            // profundidad máxima vista en la fila actual
        float cursorX    = -palletInner.x / 2f + margin;  // cursor horizontal dentro de la fila

        foreach (var piece in sorted)
        {
            // Elegir la orientación con menor altura que quepa en el pallet
            var ori = PickLowestFittingOrientation(piece, palletInner, margin);

            if (ori == null)
            {
                Debug.LogWarning($"[CubicajeEngine] Pieza '{piece.id}' no cabe en ninguna orientación. Se omite.");
                continue;
            }

            float pw = ori.Value.w;
            float ph = ori.Value.h;
            float pd = ori.Value.d;

            // ¿La pieza supera el borde derecho de la fila actual?
            if (cursorX + pw > palletInner.x / 2f - margin)
            {
                // Iniciar nueva fila dentro de la misma capa
                rowZMin  += rowMaxD + margin;
                rowMaxD   = 0f;
                cursorX   = -palletInner.x / 2f + margin;
            }

            // ¿La fila supera el borde frontal de la capa actual?
            if (rowZMin + pd > palletInner.z / 2f - margin)
            {
                // Subir a la siguiente capa
                layerYMin += layerMaxH + margin;
                layerMaxH  = 0f;
                rowZMin    = -palletInner.z / 2f + margin;
                rowMaxD    = 0f;
                cursorX    = -palletInner.x / 2f + margin;
            }

            // ¿Hay espacio vertical en la capa?
            if (layerYMin + ph > palletInner.y / 2f - margin)
            {
                Debug.LogWarning($"[CubicajeEngine] Pieza '{piece.id}': no hay espacio vertical restante. Se omite.");
                continue;
            }

            // Centro de la pieza en espacio local del pallet
            float cx = cursorX + pw / 2f;
            float cy = layerYMin + ph / 2f;
            float cz = rowZMin  + pd / 2f;

            result.Add(new PlacedPiece
            {
                id         = piece.id,
                localPos   = new Vector3(cx, cy, cz),
                localEuler = ori.Value.euler,
                scale      = new Vector3(pw, ph, pd)
            });

            // Avanzar cursor y actualizar máximos de la fila/capa
            cursorX  += pw + margin;
            rowMaxD   = Mathf.Max(rowMaxD, pd);
            layerMaxH = Mathf.Max(layerMaxH, ph);
        }

        // Ordenar la secuencia de ensamblado:
        //   Y ascendente → piso antes que techo
        //   Z ascendente → fondo del pallet antes que frente
        //   X ascendente → izquierda antes que derecha
        result.Sort((a, b) =>
        {
            int yComp = a.localPos.y.CompareTo(b.localPos.y);
            if (yComp != 0) return yComp;

            int zComp = a.localPos.z.CompareTo(b.localPos.z);
            if (zComp != 0) return zComp;

            return a.localPos.x.CompareTo(b.localPos.x);
        });

        Debug.Log($"[CubicajeEngine] Solución: {result.Count}/{sorted.Count} piezas colocadas.");

        return result;
    }

    // ===== Métodos auxiliares =====

    /// <summary>
    /// Devuelve el volumen de una pieza.
    /// </summary>
    private static float PieceVolume(PieceEntry p) =>
        p.ancho_m * p.alto_m * p.fondo_m;

    /// <summary>
    /// Elige la orientación con menor altura (Y) entre las que caben en el pallet.
    /// Evalúa las 6 orientaciones posibles de una caja rectangular.
    /// </summary>
    private static Orientation? PickLowestFittingOrientation(
        PieceEntry p, Vector3 palletInner, float margin)
    {
        float a = p.ancho_m;
        float b = p.alto_m;
        float c = p.fondo_m;

        // Las 6 permutaciones posibles de (ancho, alto, fondo) sobre los ejes (X, Y, Z),
        // con su rotación Euler aproximada para orientar el holograma al operador.
        var candidates = new Orientation[]
        {
            new Orientation { w=a, h=b, d=c, euler=new Vector3(  0f,   0f,  0f) },
            new Orientation { w=c, h=b, d=a, euler=new Vector3(  0f,  90f,  0f) },
            new Orientation { w=a, h=c, d=b, euler=new Vector3( 90f,   0f,  0f) },
            new Orientation { w=c, h=a, d=b, euler=new Vector3( 90f,  90f,  0f) },
            new Orientation { w=b, h=a, d=c, euler=new Vector3(  0f,   0f, 90f) },
            new Orientation { w=b, h=c, d=a, euler=new Vector3( 90f,   0f, 90f) },
        };

        Orientation? best = null;

        foreach (var o in candidates)
        {
            // Verificar que la pieza con este tamaño cabe dentro de los límites del pallet
            bool fitsX = o.w <= palletInner.x - 2f * margin;
            bool fitsY = o.h <= palletInner.y - 2f * margin;
            bool fitsZ = o.d <= palletInner.z - 2f * margin;

            if (!fitsX || !fitsY || !fitsZ)
                continue;

            // Preferir la orientación con menor altura (deja más espacio para capas superiores)
            if (best == null || o.h < best.Value.h)
                best = o;
        }

        return best;
    }
}
