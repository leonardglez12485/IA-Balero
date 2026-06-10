# Zafiro - Base de aprendizaje para consultas SQL

> Fuente: metadata real de `zafiro_dev` extraida desde SQL Server el 2026-06-10. No contiene secretos ni cadena de conexion.

## Proposito

Este archivo orienta a Codex/IA para generar SQL de solo lectura sobre Zafiro. La IA debe usarlo como guia semantica antes de calcular ventas, stock, compras, cobranzas, ordenes de trabajo o estadisticas comerciales.

Regla principal: si la pregunta pide datos reales, primero usar el MCP financiero y consultar la base. No inventar tablas, columnas ni codigos.

Uso en runtime: el backend no envia este archivo completo en cada pregunta. `CodexService` toma una instruccion minima y agrega solo las secciones relacionadas con la intencion detectada para reducir tokens.

## Arquitectura de datos observada

La base tiene 195 tablas de usuario. El nucleo operativo detectado es:

- `dbo.Ventas`: cabecera de comprobantes de venta.
- `dbo.VentasLineas`: lineas de venta, producto, cantidad e importes.
- `dbo.VentasMediosPago`: medios de pago por venta.
- `dbo.Clientes`: clientes y datos fiscales/comerciales.
- `dbo.Sucursales`: sucursales.
- `dbo.Vendedores`: vendedores.
- `dbo.Productos`: maestro de productos. PK real: `idProducto`.
- `dbo.ProductosCodigos`: codigos alternativos por producto.
- `dbo.CategoriasProducto`: categorias de producto.
- `dbo.Compras` y `dbo.ComprasLineas`: compras y lineas de compra.
- `dbo.AjusteStock`: movimientos/ajustes de stock con cantidad firmada.
- `dbo.MovimientoDepositos`: relacion entre ajustes de entrada/salida de deposito.
- `dbo.OrdenesTrabajo`: ordenes de trabajo del flujo optico/servicio.
- `dbo.VentaOrdenesTrabajo`: relacion venta - orden de trabajo.

Arquitectura heredada del ERP Zafiro/Balero: WebForms/WebServices -> Logic/Handlers -> Logic/Models -> DataAccess -> SQL Server/SPs. El sistema usa tablas transaccionales y muchos codigos de documento; no asumir que todo importe positivo aumenta ventas.

## Claves y joins frecuentes

Ventas:

```sql
FROM dbo.Ventas v
JOIN dbo.VentasLineas vl ON vl.VentaId = v.Id
LEFT JOIN dbo.Productos p ON p.idProducto = vl.ProductoId
LEFT JOIN dbo.Clientes c ON c.idCliente = v.ClienteId
LEFT JOIN dbo.Sucursales s ON s.id = v.SucursalId
LEFT JOIN dbo.Vendedores ve ON ve.idVendedor = v.VendedorId
```

Medios de pago:

```sql
FROM dbo.Ventas v
JOIN dbo.VentasMediosPago vmp ON vmp.VentaId = v.Id
```

Ordenes de trabajo facturadas:

```sql
FROM dbo.VentaOrdenesTrabajo vot
JOIN dbo.Ventas v ON v.Id = vot.VentaId
JOIN dbo.OrdenesTrabajo ot ON ot.Id = vot.OrdenTrabajoId
```

Compras:

```sql
FROM dbo.Compras co
JOIN dbo.ComprasLineas cl ON cl.CompraId = co.Id
LEFT JOIN dbo.Productos p ON p.idProducto = cl.ProductoId
```

Stock/ajustes:

```sql
FROM dbo.AjusteStock ast
LEFT JOIN dbo.Productos p ON p.idProducto = ast.idProducto
LEFT JOIN dbo.Sucursales s ON s.id = ast.SucursalId
LEFT JOIN dbo.Depositos d ON d.idDeposito = ast.DepositoId
```

## Reglas de ventas y notas de credito

`Ventas.TipoDoc` es critico para cualquier estadistica de ventas. En la muestra real aparecieron:

- `101`: e-Ticket / venta. Suma.
- `111`: e-Factura / venta. Suma.
- `121`: documento de venta/exportacion. Suma salvo regla fiscal mas especifica.
- `001`, `011`, `201`: documentos legacy/de venta. Suman, salvo validacion funcional especifica.
- `102`: nota de credito e-Ticket. Resta.
- `112`: nota de credito e-Factura. Resta.
- `122`: nota de credito de documento tipo 121. Resta.
- `002`: probable nota de credito legacy. Tratar como resta si el contexto es venta neta.
- `103`: nota de debito e-Ticket. Suma.

Regla recomendada para venta neta:

```sql
CASE
  WHEN v.TipoDoc IN ('102', '112', '122', '002') THEN -1
  ELSE 1
END
```

Aplicar el signo sobre importes y cantidades cuando el KPI represente venta neta:

```sql
SUM(
  CASE WHEN v.TipoDoc IN ('102','112','122','002') THEN -1 ELSE 1 END
  * ISNULL(vl.TotalBruto, 0)
) AS VentaBrutaNeta
```

Para ventas brutas no neteadas, informar explicitamente que no se restaron notas de credito.

## Filtros de validez

Para consultas comerciales normales:

```sql
WHERE ISNULL(v.Anulada, 0) = 0
  AND ISNULL(v.Rechazada, 0) = 0
```

No asumir que `EstadoCFE` siempre tiene valor; en la muestra habia muchos registros vacios. Si se analiza fiscalidad/CFE, mostrar desglose por `EstadoCFE`, `Rechazada`, `CodRechazo` y `MotRechazo`.

Fechas:

- Ventas: `Ventas.Fecha`.
- Compras: `Compras.Fecha`.
- Stock: `AjusteStock.Fecha`.
- Ordenes de trabajo: revisar `OrdenesTrabajo` y elegir fecha segun pregunta (`Fecha`, estados, historico si aplica).

## Stock y rotacion

No usar `Productos` como tabla de stock actual: no tiene columna `Stock`. Para movimientos observados usar:

- `AjusteStock.Cantidad`: cantidad firmada.
- `AjusteStock.idProducto`: producto.
- `AjusteStock.SucursalId`: sucursal.
- `AjusteStock.DepositoId`: deposito.
- `AjusteStock.Fecha`: fecha del ajuste/movimiento.

Para ventas de productos usar `VentasLineas.Cantidad` con signo por `Ventas.TipoDoc`.

Formula base para rotacion de stock, si no hay tabla especifica de saldo historico:

```sql
UnidadesVendidasNetas = SUM(signo_tipo_doc * VentasLineas.Cantidad)
StockMovimientoNeto = SUM(AjusteStock.Cantidad)
```

Si el usuario pide rotacion clasica:

```text
rotacion = unidades vendidas netas del periodo / stock promedio
```

Pero si no existe stock promedio historico en el esquema disponible, responder con una aproximacion y decirlo:

- usar movimientos/ajustes para estimar existencia,
- o pedir/identificar tabla de inventario/saldo si aparece en el esquema.

## Compras

`Compras.TipoDoc` tambien requiere signo fiscal:

- `111`, `101`, `001`: compra/factura. Suman.
- `112`, `102`, `002`: nota de credito de compra. Restan.

Usar `ISNULL(Compras.Anulada,0)=0` para compras vigentes.

## Consultas patron

Ventas netas por mes:

```sql
SELECT
  YEAR(v.Fecha) AS Anio,
  MONTH(v.Fecha) AS Mes,
  SUM(CASE WHEN v.TipoDoc IN ('102','112','122','002') THEN -1 ELSE 1 END * ISNULL(v.TotalBruto,0)) AS VentaBrutaNeta
FROM dbo.Ventas v
WHERE ISNULL(v.Anulada,0)=0
  AND ISNULL(v.Rechazada,0)=0
GROUP BY YEAR(v.Fecha), MONTH(v.Fecha)
ORDER BY Anio, Mes;
```

Top productos por venta neta:

```sql
SELECT TOP (20)
  p.idProducto,
  p.Codigo,
  p.Nombre,
  SUM(CASE WHEN v.TipoDoc IN ('102','112','122','002') THEN -1 ELSE 1 END * ISNULL(vl.Cantidad,0)) AS UnidadesNetas,
  SUM(CASE WHEN v.TipoDoc IN ('102','112','122','002') THEN -1 ELSE 1 END * ISNULL(vl.TotalBruto,0)) AS ImporteBrutoNeto
FROM dbo.Ventas v
JOIN dbo.VentasLineas vl ON vl.VentaId = v.Id
JOIN dbo.Productos p ON p.idProducto = vl.ProductoId
WHERE ISNULL(v.Anulada,0)=0
  AND ISNULL(v.Rechazada,0)=0
GROUP BY p.idProducto, p.Codigo, p.Nombre
ORDER BY ImporteBrutoNeto DESC;
```

Ventas por sucursal y vendedor:

```sql
SELECT
  s.Descripcion AS Sucursal,
  ve.Nombre AS Vendedor,
  SUM(CASE WHEN v.TipoDoc IN ('102','112','122','002') THEN -1 ELSE 1 END * ISNULL(v.TotalBruto,0)) AS VentaBrutaNeta
FROM dbo.Ventas v
LEFT JOIN dbo.Sucursales s ON s.id = v.SucursalId
LEFT JOIN dbo.Vendedores ve ON ve.idVendedor = v.VendedorId
WHERE ISNULL(v.Anulada,0)=0
  AND ISNULL(v.Rechazada,0)=0
GROUP BY s.Descripcion, ve.Nombre
ORDER BY VentaBrutaNeta DESC;
```

## Archivos de respaldo generados

La metadata completa compacta esta en:

- `FinancialChat/Knowledge/schema-tables.csv`
- `FinancialChat/Knowledge/schema-columns.csv`
- `FinancialChat/Knowledge/schema-foreign-keys.csv`
- `FinancialChat/Knowledge/schema-domain-candidates.csv`

Si una consulta requiere una tabla no documentada en este markdown, usar esos CSV o la tool `ObtenerEsquemaBaseDatos` antes de generar SQL.
