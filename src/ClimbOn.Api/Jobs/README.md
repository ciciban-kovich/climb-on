# Jobs

Each job is an `IJob` registered in DI and run through `JobRunner` (design §5).

## `postcodes` (S99–S102)

Imports Kartverket's Postnummerområder (C2, CC BY 4.0) monthly and logs one
`PostcodeImportCompleted` event with the counts added, changed, retired, reactivated and active
total (C32: counts only).

### Dataset

| | |
|---|---|
| Source | Geonorge, dataset `462a5297-33ef-438a-82a5-07fff5799be3`, area "Hele landet" (0000) |
| URL | `KartverketSource.DatasetUri`: a static file, no order needed. The Geonorge order API (`nedlasting.geonorge.no/api/order`) returns the same file |
| Format | A zip with one GeoJSON file. It is not a plain FeatureCollection: the top level is an object with two, `postnummeromrader.postnummeromrade` (the areas, read) and `postnummeromrader.postnummeromradegrense` (boundary lines, skipped) |
| Coordinate reference system | EPSG:4258 (EUREF89 geographic, lon/lat degrees), given in the pre-RFC 7946 `crs` member. Geonorge also offers UTM 32/33/35 and LAEA; 4258 is chosen so no reprojection is needed. `KartverketSource` rejects any other `crs`. ETRS89 is treated as WGS 84 (they differ by well under a metre) |
| Postcode | Property `postnummer` (four digits). A postcode with several parts is several `Polygon` features; they are combined, and the interior point of the whole is taken (S99) |
| Size (2026-09-29 extract) | 8.9 MB zipped, 62.8 MB unzipped; 3,450 features, 3,382 postcodes, about 1.09 million coordinates |
| Extent | Includes Svalbard (lat about 79) and Jan Mayen (lon about −8), so not every point is in mainland Norway |
| Updates | Monthly, except July and December |

PO-box-only postcodes are not in this dataset; a feature without a geometry is left out too (S100).

### Measured cost of one run

Measured on 2026-10-19 on Windows, .NET 10, Release build, calling `KartverketSource.LoadAsync`
against the live URL (download, unzip, parse, interior points):

| | |
|---|---|
| Duration | 2.0 s |
| Peak working set (whole process) | 130 MB |
| Managed heap after parse | 65 MB |
| With the GC heap capped at 96 MB (`DOTNET_GCHeapHardLimit`) | completes |

The diff and apply on top of this is about 3,400 rows in one transaction.

`KartverketSource` refuses an archive over 32 MiB, a GeoJSON file over 192 MiB and a download
(body included) that takes over 5 minutes, so the job fails with the table unchanged (S101.2).
These caps limit the bytes read, not the memory the parse uses: a GeoJSON file under the cap can
still need more memory than the job has.

### Fixtures

`tests/ClimbOn.Integration.Tests/Fixtures/kartverket/postnummeromrader-v1.geojson` is an excerpt of
the 2026-09-29 file: 10 features (8 postcodes, among them two with two parts and one with a hole)
and 3 boundary lines, with values unchanged. `-v2` is the same excerpt edited to act as a later version.
`tests/ClimbOn.Infrastructure.Tests` reads the same files for the parsing tests.
