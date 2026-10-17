# 🧩 Muestras de Código C# de Infraestructura (samples/)

Esta carpeta incluye **4 motores compartidos reales (AddOns/)** extraídos sin modificaciones de la suite privada **Pure NT8 Suite** (~13.000 LOC en 54 archivos C#) para ilustrar la arquitectura en C#, el modelado matemático de microestructura de mercado y el diseño de baja latencia sin exponer los 32 indicadores propietarios ni las 11 columnas de SuperDOM:

| Archivo | Ruta original | Qué demuestra |
| :--- | :--- | :--- |
| [PureTopOfBook.cs](PureTopOfBook.cs) | AddOns/PureTopOfBook.cs | **Microestructura L1 Académica (C# puro, sin dependencias NT8):** Cálculo en tiempo real de *Order Flow Imbalance* (**OFI**, Cont, Kukanov & Stoikov 2014), **Microprecio** ponderado por volumen (Stoikov 2018) y desequilibrio de cola. |
| [PureDepthBook.cs](PureDepthBook.cs) | AddOns/PureDepthBook.cs | **Ingeniería de Libro L2 y Anti-Spoofing:** Replicación completa del libro de órdenes por posición (Insert/Update/Remove) para eliminar niveles fantasma cuando una fila sale del top-*N*, con seguimiento de antigüedad por lote (AgeLot) para filtrar órdenes efímeras (*quote-stuffing* / *spoofing*). |
| [PureTimeCalibration.cs](PureTimeCalibration.cs) | AddOns/PureTimeCalibration.cs | **Calibración Estacional Intradía:** Normalización adaptativa de umbrales de volumen y velocidad del tape en cubetas de 30 minutos para compensar la curva en *U* de actividad intradía. |
| [PureCsvWriter.cs](PureCsvWriter.cs) | AddOns/PureCsvWriter.cs | **Concurrencia y No-Bloqueo del Hilo de Datos:** Cola asíncrona encadenada con Task e InvariantCulture que descarga la escritura a disco fuera de OnMarketData, evitando congelamientos del gráfico cuando OneDrive bloquea ficheros durante la sincronización. |