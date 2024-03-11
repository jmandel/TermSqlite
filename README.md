
## Preparing Vocab Fixtures

```sh
zcat fhir-concept-publication-demo/CodeSystem-rxnorm-03072022.ndjson.gz |\
grep -iE "RxNormCurrentPrescribableContent|lorat|clari" |\
gzip > fixtures/CodeSystem-rxnorm-claritin-only.ndjson.gz
```
