# 📘 Catálogo Técnico Completo — Suite Pure (NinjaTrader 8)

Referencia detallada de las **54 piezas en C# (~13.000 LOC)** que componen el ecosistema **Pure** para NinjaTrader 8 (probado sobre NinjaTrader `8.1.7.2`).

---

## 🧭 Resumen de Requisitos de Datos por Herramienta

Para evitar configuraciones erróneas en tiempo real o en histórico, cada herramienta declara explícitamente qué nivel de datos consume:

* **Barras Estándar (Histórico sin Tick Replay):** Calcula sobre OHLCV de barra o `BarsRequest` interno. Funciona inmediatamente al abrir el gráfico.
* **Nivel 1 (Tape / Prints `OnMarketData`):** Clasifica agresor compra/venta e inspecciona el *best bid / best ask*. Para reconstruir el histórico pasado exige **Tick Replay** activo en el *Data Series*; sin él, calcula desde que arranca el gráfico en tiempo real.
* **Nivel 2 (Profundidad de Libro `OnMarketDepth`):** Opera sobre la cola completa de órdenes límite por posición (`PureDepthBook`). Solo funciona en **tiempo real** (o *Playback* con datos L2 grabados), ya que NinjaTrader no almacena histórico L2 en `Tick Replay`.

---

## 1. Indicadores de Gráfico (`Indicators/Pure/` — 32 Estudios)

### 1.1 Microestructura L1, Cinta (*Tape*) y Flujo de Órdenes

| Indicador | Tipo | Datos Requeridos | Publica a `PureSignalHub` | Descripción Técnica y Lógica de Cálculo |
| :--- | :---: | :---: | :---: | :--- |
| **`PureL1Flow`** | Panel | Nivel 1 (Top-of-Book) | `Microstructure` | Implementa tres modelos académicos de microestructura sobre el mejor nivel (`PureTopOfBook`): **OFI** (*Order Flow Imbalance*, Cont, Kukanov & Stoikov 2014), **Microprecio** (Stoikov 2018, desviación en ticks respecto al precio medio ponderada por desequilibrio de cola) y **Queue Imbalance** ($[-1, +1]$). Expone las tres series (`Ofi`, `MicroDevTicks`, `QueueImb`) para estrategias automatizadas. |
| **`PureTapeSpeed`** | Panel | Nivel 1 (Tape) | `TapeBurst` | Velocidad de la cinta en *prints por segundo* sobre ventana rodante comparada contra una línea base EMA temporal (`PureTimeEma`) calibrada por hora del día (`PureTimeCalibration`). Colorea según el signo del delta agresor y detecta estallidos (*bursts* $\ge \text{BurstMultiple} \times \text{baseline}$). |
| **`PureSweepDetector`** | Overlay | Nivel 1 (Tape) | `Sweep` | Reconstruye órdenes de mercado institucionales fragmentadas (`PureOrderGroup`) que barren múltiples niveles de precio consecutivos en milisegundos. Trazado de flechas direccionales en el precio y alerta sonora opcional. |
| **`PureVolumeDeltaFiltered`** | Panel | Nivel 1 / Tick Replay | — | Delta de volumen filtrando órdenes consolidadas por tamaño en **hasta 3 rangos simultáneos configurables** (ej. flujo minorista vs. lotes institucionales). Soporta modo por barra o acumulado, y conteo de órdenes o volumen. |
| **`PureInventoryCostBasis`** | Overlay | Nivel 1 / Tick Replay | `EffortResult` | Calcula dos VWAP separados del tape desde el inicio de sesión: el *cost basis* de los compradores agresivos y el de los vendedores agresivos. Cuando el precio cae por debajo del coste medio comprador (o supera el vendedor), detecta inventario atrapado (*trapped inventory*). |
| **`PureFeedDelay`** | Panel + Badge | Nivel 1 (Tiempo real) | — | Mide el desfase entre el timestamp oficial del exchange (`e.Time`) y el reloj local en cada print, comparándolo con su línea base EMA para detectar degradación o congelamiento del feed de datos durante picos de volatilidad. |

---

### 1.2 Dinámica del Libro de Órdenes Nivel 2 (DOM / Liquidez en Reposo)

| Indicador | Tipo | Datos Requeridos | Publica a `PureSignalHub` | Descripción Técnica y Lógica de Cálculo |
| :--- | :---: | :---: | :---: | :--- |
| **`PureRestingImbalance`** | Panel ($-100..+100$) | Nivel 2 (`PureDepthBook`) | `BookImbalance` | Oscilador del desequilibrio de liquidez en reposo sobre los mejores $N$ niveles con **filtro anti-spoofing / quote-stuffing**: cada incremento de volumen registra su marca temporal y solo computa en el ratio cuando ha persistido en el libro más de `MinLifetimeMs` milisegundos. |
| **`PurePullStack`** | Panel | Nivel 2 + Tape L1 | `PullStack` | Mide los cambios netos de liquidez por lado usando `PurePullStackEngine`: separa el **stacking** (liquidez añadida defendiendo precio) del **pulling** (cancelaciones reales, descontando el volumen que desapareció porque fue ejecutado en el tape). |
| **`PureLargestRestingOrders`** | Panel + Overlay | Nivel 2 | — | Rastrea y grafica el tamaño de la mayor orden límite visible en el *bid* y en el *ask* dentro de los primeros $N$ niveles, marcando en el gráfico la aparición de murallas institucionales por encima del umbral. |
| **`PureLevel2Tape`** | Overlay | Nivel 2 + Tape L1 | — | Cinta visual sobre las velas de eventos significativos de profundidad: marca apariciones de grandes bloques (`Add >= AppearThreshold`) y retiradas masivas sin ejecutar (`Pull >= PullThreshold`), distinguiendo absorción de cancelación. |
| **`PureLevel2Changes`** | Panel | Nivel 2 | — | Tasa de actividad del libro L2 (eventos `Add`, `Update` y `Remove` por segundo en ventana rodante) como indicador adelantado de explosiones de volatilidad antes de que el volumen ejecutado despegue. |
| **`PureLiquidityVacuum`** | Panel ($0..100$) | Nivel 1 + Nivel 2 | `Vacuum` *(Estado)* | **Filtro de «No Operar»:** detecta cuándo los *market makers* retiran liquidez antes de movimientos violentos combinando tres componentes contra sus EMAs de largo plazo: ensanchamiento del spread, caída del ritmo de actualización de *quotes* y adelgazamiento del libro L2. Evaluado en cada quote y mediante temporizador cada 250 ms. |

---

### 1.3 Huella Intrabarra (*Footprint*), Absorción y Divergencias de Esfuerzo

| Indicador | Tipo | Datos Requeridos | Publica a `PureSignalHub` | Descripción Técnica y Lógica de Cálculo |
| :--- | :---: | :---: | :---: | :--- |
| **`PureAbsorptionChart`** | Overlay | Footprint L1 o L2 Refill | `Absorption` | Detecta absorción pasiva en el gráfico mediante dos motores seleccionables: **1) Footprint (L1 / Tick Replay):** alto volumen agresivo en los extremos de la vela encerrado con rechazo al cierre; **2) L2 Refill:** recarga pasiva de contratos en el libro tras ejecuciones en el tape. |
| **`PureAuction`** | Overlay | Footprint L1 / Tick Replay | `StackedImbalance` | Analiza la matriz bid/ask por precio de cada vela (`PureFootprint`): **1) Imbalances diagonales apilados** ($\ge 3$ niveles consecutivos donde el agresor barre al lado opuesto, proyectados como zonas de S/R); **2) Subastas sin terminar (*Unfinished Auctions*)** en extremos de barra sin cero en el lado opuesto. |
| **`PureDeltaTail`** | Panel + Overlay | Footprint L1 / Tick Replay | `DeltaTail` | Explota `MaxDelta` y `MinDelta` intrabarra de `PureFootprintBar`: detecta **mechas de delta (*Delta Flips*)** cuando durante la vela el delta acumulado alcanza una excursión extrema ($\ge \text{MinExcursion}$) pero antes del cierre devuelve al menos `MinGivebackRatio` (ej. 80%), atrapando a los agresores. |
| **`PureEffortResult`** | Panel + Overlay | Nivel 1 (Tape) | `EffortResult` | Divergencia **Esfuerzo vs. Resultado**: identifica ventanas rodantes con volumen agresivo extremo (calibrado por hora del día) pero avance de precio casi nulo en ticks, marcando triángulos de giro por absorción. |
| **`PureDeltaDivergence`** | Panel + Overlay | Nivel 1 / Tick Replay | `DeltaDivergence` | Detecta giros estructurales (zigzag por reversal de $N$ ticks, `PureSwingTracker`) y compara el delta acumulado exacto entre extremos de cada tramo: marca divergencia cuando el precio hace un máximo más alto con menor delta comprador (o mínimo más bajo con menor delta vendedor). |

---

### 1.4 Perfil de Volumen (*Volume Profile*), VWAP y *Auction Market Theory*

| Indicador | Tipo | Datos Requeridos | Publica a `PureSignalHub` | Descripción Técnica y Lógica de Cálculo |
| :--- | :---: | :---: | :---: | :--- |
| **`PureValueArea`** | Overlay | Barras (Sin Tick Replay) | — | Calcula y traza en desarrollo el **POC**, **VAH** y **VAL** de la sesión actual (`PureVolumeProfile`) más las líneas horizontales de los niveles de valor de las últimas $N$ sesiones completadas. |
| **`PureVolumeNodes`** | Overlay | Barras (Sin Tick Replay) | `Retest` | Topología interna del perfil de volumen: detecta automáticamente **LVNs** (*Low Volume Nodes* / valles de rechazo y aceleración) y **HVNs** secundarios sobre la sesión actual y previa. Publica señal `Retest` al hub cuando el precio rechaza un LVN al cierre de barra. |
| **`PureVwapSuite`** | Overlay | Barras (Sin Tick Replay) | — | Suite institucional todo-en-uno: VWAP de sesión con bandas de $\pm 1\sigma$ y $\pm 2\sigma$ ponderadas por volumen, VWAP semanal y mensual, hasta 3 VWAPs anclados rodantes a $N$ sesiones atrás y VWAP con anclaje manual por fecha/hora. |
| **`PureInitialBalance`** | Overlay + Badge | Barras (Sin Tick Replay) | — | **Initial Balance (IB)** y **Opening Range (OR)** configurables con proyecciones de extensión ($\pm 0{,}5\times, 1{,}0\times, 1{,}5\times, 2{,}0\times$) y clasificación estadística del ancho del IB de hoy frente a la media histórica (estrecho $< 0{,}75\times$ = potencial tendencial; amplio $> 1{,}3\times$ = rotacional). |
| **`PureLegProfiles`** | Overlay | Barras + Tape opcional | — | Perfiles de volumen automáticos tramo a tramo (*leg-to-leg*) sobre swings zigzag: dibuja el rectángulo del área de valor y el POC de cada impulso o corrección, añadiendo en vivo la etiqueta del delta agresor del tramo. |
| **`PureSessionLevels`** | Overlay | Barras (Sin Tick Replay) | — | Máximo, mínimo y apertura de hasta **3 ventanas horarias/killzones** (Asia, Londres, NY) con conversión automática de zona horaria de Windows y horario de verano (*DST*), más OHLC de la sesión previa y *Settlement*. |
| **`PureSwingZones`** | Overlay | Barras (Sin Tick Replay) | — | Agrupa (*clustering*) giros de swing históricos que caen a menos de `ZoneTicks` entre sí para construir zonas de soporte/resistencia cuya opacidad escala según el número de toques recibidos. |
| **`PureForecastRange`** | Overlay | Barras (Sin Tick Replay) | — | Proyecta el máximo y mínimo esperados del día midiendo qué fracción del rango diario final suele haberse completado históricamente a la hora actual en bloques de $N$ minutos. |

---

### 1.5 Amplitud de Mercado (*Breadth*), Fuerza Relativa y Régimen

| Indicador | Tipo | Datos Requeridos | Publica a `PureSignalHub` | Descripción Técnica y Lógica de Cálculo |
| :--- | :---: | :---: | :---: | :--- |
| **`PureTick`** | Panel | Índice `TICK` o Cesta | `BreadthExtreme` | Amplitud de mercado con doble motor: **1) `RealIndex`** (`TICK` NYSE, `TICKQ` Nasdaq, `TICKDJ` Dow); **2) `Basket`** (calcula un mini-TICK sintético en tiempo real contando *upticks* menos *downticks* sobre una cesta configurable de instrumentos). Publica señales contrarias (*fade*) en extremos de agotamiento. |
| **`PureTickDivergence`** | Panel + Overlay | Índice `TICK` o Cesta | `TickDivergence` | Detecta divergencias clásicas (giro) y ocultas (continuación) entre los swings del precio y la amplitud (`CumulativeTick`, `InstantTick` o `TickDelta` por barra). |
| **`PureRelativeStrength`** | Panel | Barras (2 instrumentos) | `RelativeStrength` | Diferencia de variación porcentual desde la apertura de sesión entre un instrumento de referencia y el activo del gráfico (ej. `MES` vs `MNQ`), detectando liderazgos y divergencias inter-mercado. |
| **`PureRegime`** | Panel + Badge | Barras (Sin Tick Replay) | `Regime` *(Estado)* | Clasifica el contexto del mercado mediante tres métricas sin *look-ahead*: **VolRatio** ($\text{ATR actual} / \overline{\text{ATR}}$), **VolumeRatio** (volumen actual frente a la media histórica de ese mismo minuto del día) y **Efficiency Ratio** de Kaufman (tendencia vs. *chop*). |

---

### 1.6 Confluencia, Motor de Alertas y Auditoría Estadística (*Scorecard*)

| Indicador | Tipo | Datos Requeridos | Rol en `PureSignalHub` | Descripción Técnica y Lógica de Cálculo |
| :--- | :---: | :---: | :---: | :--- |
| **`PureConfluence`** | Panel ($-100..+100$) | Eventos `PureSignalHub` | Consumidor | Sintetiza todas las señales direccionales frescas del instrumento en una única puntuación ponderada con **decaimiento exponencial** (vida media configurable), escalada por el consenso mínimo de fuentes (`MinAgreeingSources`) y amortiguada si `PureLiquidityVacuum` reporta vacío de liquidez. |
| **`PureAlertEngine`** | Invisible | Eventos `PureSignalHub` | Consumidor (Singleton por activo) | Convierte las señales del bus en alertas nativas de NinjaTrader (sonido + ventana *Alerts*). Incluye *guard* automático en el hub para que, si varios gráficos del mismo instrumento tienen el motor cargado, solo alerte uno y el siguiente tome el relevo si se cierra. |
| **`PureSignalScorecard`** | Overlay (Tabla) | Eventos `PureSignalHub` + L1 | Auditor Cuantitativo | Evalúa empíricamente y en tiempo real cada señal publicada en el bus midiendo sobre el *mid-price* la excursión en ticks a **+5 s, +30 s, +1 min y +5 min**, **MFE/MAE**, porcentaje de acierto, **estadístico $t$ a 1 minuto** y expectativa neta tras descontar `CostTicks`. Exporta registros a CSV asíncrono (`PureCsvWriter`). |

---

## 2. Columnas para SuperDOM (`SuperDomColumns/` — 11 Columnas)

Todas las columnas heredan de `PureColumnBase` y están optimizadas para renderizado de baja latencia por fila:

| Columna SuperDOM | Descripción Funcional |
| :--- | :--- |
| **`PureAbsorption`** | Cruza ejecuciones del tape (L1) con recargas de profundidad (L2) por precio para estimar órdenes *iceberg* / absorción pasiva y publica `PureSignalSource.Absorption` al hub. |
| **`PureBookState`** | Muestra la profundidad visible por nivel, tinta huecos de liquidez (*liquidity gaps* anómalamente finos) y muestra en la banda inferior el ratio de desequilibrio agregado de las mejores $N$ filas. |
| **`PureConsolidatedTape`** | Filtra el *Time & Sales* por nivel mostrando únicamente órdenes consolidadas institucionales ($\ge \text{MinOrderSize}$) con contador de impactos y volumen acumulado (ej. `3x 210`). |
| **`PureDomContext`** | Trae el contexto del gráfico directamente a la escalera del SuperDOM leyendo del `PureSignalHub` el score de `PureConfluence`, el régimen de `PureRegime` y la alerta de `PureLiquidityVacuum`. |
| **`PureDwellTime`** | Mide el tiempo acumulado que cada nivel de precio ha permanecido como *Best Bid* o *Best Ask* durante la sesión (identifica aceptación frente a rechazo rápido). |
| **`PureLastExecutedTrades`** | Volumen ejecutado en *Bid* y *Ask* por nivel de precio con barra de delta horizontal, auto-limpieza configurable por tiempo o desplazamiento en ticks e intercambio de columnas. |
| **`PurePullStackColumn`** | Vista nivel a nivel del *stacking* (liquidez añadida) y *pulling* (cancelaciones netas descontando ejecuciones) con decaimiento exponencial (`PurePullStackEngine`). |
| **`PureQueuePosition`** | **Estimador conservador de posición en cola** para tus órdenes límite activas (*working orders*): registra la profundidad delante de tu orden al entrar y descuenta cada ejecución en ese nivel (`~N` contratos por delante + barra de progreso). |
| **`PureSessionDelta`** | Perfil de delta acumulado de toda la sesión por precio sin reinicios: identifica en qué niveles exactos ganaron compradores o vendedores agresivos desde la apertura. |
| **`PureSessionProfile`** | Perfil de volumen de sesión en la escalera del DOM (cargado mediante `BarsRequest` de 1 tick + actualización en vivo) con POC destacado y área de valor (VAH/VAL) tintada. |
| **`PureStopProjection`** | Inferencia estructural de zonas de *buy stops* y *sell stops* sobre máximos/mínimos de swing recientes (`SwingMinutes`) y niveles psicológicos / números redondos. |

---

## 3. Motores Compartidos (`AddOns/` — 10 Módulos) y Herramientas (`DrawingTools/` — 1)

| Archivo | Propósito de Ingeniería |
| :--- | :--- |
| **`AddOns/PureSignalHub.cs`** | Bus *pub/sub* estático y *thread-safe* particionado por instrumento que comunica indicadores de gráfico y columnas SuperDOM dentro de `NinjaTrader.Custom.dll`. |
| **`AddOns/PureDepthBook.cs`** | Réplica completa del libro L2 indexada por posición (evita niveles fantasma cuando órdenes entran/salen del top $N$) con seguimiento de antigüedad por lote para filtrado *anti-spoofing*. |
| **`AddOns/PureTopOfBook.cs`** | Motor matemático puro (sin dependencias de NinjaTrader) para cálculo de *Order Flow Imbalance* (OFI), Microprecio de Stoikov y desequilibrio de primer nivel. |
| **`AddOns/PureFootprint.cs`** | Estructura matricial bid/ask por nivel de precio y barra con cálculo en vivo de `MaxDelta`, `MinDelta`, POC de barra y secuencias de imbalances diagonales. |
| **`AddOns/PurePullStackEngine.cs`** | Motor compartido de *pulling* y *stacking* que concilia variaciones del libro L2 con ejecuciones del tape L1 para no confundir órdenes consumidas con cancelaciones. |
| **`AddOns/PureScorecard.cs`** | Motor estadístico de evaluación multi-horizonte (+5s, +30s, +1m, +5m, MFE, MAE, estadístico $t$) desacoplado de la interfaz gráfica. |
| **`AddOns/PureShared.cs`** | Primitivas comunes: `PureTapeReader` (clasificador de agresor), `PureVolumeProfile` (POC/VAH/VAL), `PureOrderGroup` (reconstrucción de sweeps), `PureTimeEma` (EMA en tiempo de reloj) y `PureBarSplit`. |
| **`AddOns/PureTimeCalibration.cs`** | Calibrador intradía en memoria por bloques de 30 minutos para normalizar umbrales de volumen y velocidad según la estacionalidad horaria de la sesión. |
| **`AddOns/PureCsvWriter.cs`** | Cola asíncrona encadenada por `Task` para escritura de telemetría CSV fuera del hilo de datos de mercado, evitando bloqueos de UI cuando OneDrive sincroniza la carpeta de NinjaTrader 8. |
| **`AddOns/PureColumnBase.cs`** | Clase base abstracta para las 11 columnas SuperDOM con caché de plumas/pinceles WPF y utilidades de trazado por fila. |
| **`DrawingTools/PureAnchoredVwap.cs`** | Herramienta de dibujo interactiva de un clic para anclar un VWAP dinámico a cualquier vela (swing, noticia o apertura) y re-anclarlo arrastrando el nodo. |
