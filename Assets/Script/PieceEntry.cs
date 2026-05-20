using System;
using System.Collections.Generic;


[Serializable]
public class PieceEntry
{
    public string id;       
    public float alto_m;    
    public float ancho_m;   
    public float fondo_m;  
}

[Serializable]
public class PieceListWrapper
{
    public List<PieceEntry> piezas = new List<PieceEntry>();
}
