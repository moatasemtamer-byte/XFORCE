# PDF output reproducibility

Printing the same `.pt2x` twice used to produce PDFs that differed only in metadata, which
showed up as a small file-size difference:

| field | old behaviour | now |
|---|---|---|
| `Info /Title` | the input file name (`…_project_fixed.pt2x` vs `…PT2X`) | first line of the project description (content-derived) |
| `Info /CreationDate` | print time | `ProjectInfo/ProjectInfo.Xml` `ProjectCreatedDate` |
| font subset tag | PDFsharp random 6-letter prefix (`RCKWTS+Arial`) | normalised to `PT2FNT+Arial` |
| trailer `/ID` | random GUID | fixed deterministic id |

With those four values normalised, two prints of the same project are **byte-identical**
(SHA-256), regardless of the source file name. The visible pages were already identical:
text positions and every line/rectangle segment matched exactly, and the decompressed page
content streams were identical apart from the normalisation above.

Verified with `SULFUR1__2__project_fixed.pt2x` printed under two different file names:
both `PT2X_print_A.pdf` and `PT2X_print_B.pdf` have
`9DF2A76EF369AA49944FE44CFFFB7FC64EA911B7B34AB049962283757A75E963`.
