#!/bin/bash

# Define your variables
POPULATE_SCRIPT_FILENAME=${POPULATE_SCRIPT_FILENAME:-"./populate.sqlite"}
NDJSON_GZ_FILENAME=${NDJSON_FILENAME:-"./fhir-concept-publication-demo/CodeSystem-rxnorm-03072022.ndjson.gz"}
OUTPUT_DB_FILENAME=${OUTPUT_DB_FILENAME:-"./rxnorm.fhir.db"}
TEMP_NDJSON_FILE=$(mktemp)

# Ensure the temporary file is deleted on script exit or interruption
trap 'rm -f "$TEMP_NDJSON_FILE"' EXIT

# Unzip the .ndjson.gz file to the temporary file
gzip -cd "$NDJSON_GZ_FILENAME" > "$TEMP_NDJSON_FILE"

export NDJSON_FILENAME="$TEMP_NDJSON_FILE"
export SCHEMA_FILENAME=${SCHEMA_FILENAME:-"./schema.sqlite"}

echo ${SCHEMA_FILENAME}
echo $(envsubst < ${POPULATE_SCRIPT_FILENAME})

# Substitute variables in your .sqlite file and execute with sqlite3
envsubst < ${POPULATE_SCRIPT_FILENAME} | sqlite3 ${OUTPUT_DB_FILENAME}
rm ${TEMP_NDJSON_FILE}
