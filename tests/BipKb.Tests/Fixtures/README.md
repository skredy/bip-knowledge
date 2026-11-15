# Fixtures

Synthetic workbooks authored for these tests, no customer data.
Both have sheet `Migration`, A1 = `Commande`, C2 = `Cible`.

- `study.xls`: BIFF8 workbook generated with xlwt 1.3.0.
- `study.xlsx`: OOXML workbook generated with openpyxl 3.1.5.

Generators are not runtime or test dependencies. Draw.io, SQL, JSON, C#, Markdown
and PNG fixtures are generated within the tests; compressed Draw.io uses raw
DEFLATE of URL-encoded XML, matching the native format.
