#!/bin/bash

# Create the first SQLite file with medications data
sqlite3 medications.sqlite <<EOF
CREATE TABLE CodeSystems (
  name TEXT UNIQUE,
  url TEXT
);
INSERT INTO CodeSystems (name, url) VALUES ('meds', 'http://meds.example.org');
INSERT INTO CodeSystems (name, url) VALUES ('meds2', 'http://meds2.example.org');
INSERT INTO CodeSystems (name, url) VALUES ('meds3', 'http://meds3.example.org');

CREATE TABLE Concepts (
  id INTEGER PRIMARY KEY,
  code TEXT UNIQUE,
  display TEXT
);

CREATE TABLE Properties (
  id INTEGER PRIMARY KEY,
  source_code TEXT,
  property_code TEXT,
  target TEXT,
  type TEXT CHECK (type IN ('string', 'number', 'code')),
  FOREIGN KEY (source_code) REFERENCES Concepts(code)
);


INSERT INTO Concepts (code, display) VALUES
  ('MED001', 'Aspirin'),
  ('MED002', 'Ibuprofen'),
  ('MED003', 'Acetaminophen'),
  ('MED004', 'Amoxicillin'),
  ('MED005', 'Lisinopril'),
  ('MED006', 'Metformin'),
  ('MED007', 'Atorvastatin'),
  ('MED008', 'Levothyroxine'),
  ('MED009', 'Omeprazole'),
  ('MED010', 'Simvastatin'),
  ('MED011', 'Painkiller');

INSERT INTO Properties (source_code, property_code, target, type) VALUES
  ('MED001', 'PARENT', 'MED011', 'code'),
  ('MED002', 'PARENT', 'MED011', 'code'),
  ('MED001', 'DOSAGE', '81mg', 'string'),
  ('MED001', 'FORM', 'Tablet', 'string'),
  ('MED002', 'DOSAGE', '200mg', 'string'),
  ('MED002', 'FORM', 'Capsule', 'string'),
  ('MED003', 'DOSAGE', '500mg', 'string'),
  ('MED003', 'FORM', 'Tablet', 'string'),
  ('MED004', 'DOSAGE', '250mg', 'string'),
  ('MED004', 'FORM', 'Capsule', 'string'),
  ('MED005', 'DOSAGE', '10mg', 'string'),
  ('MED005', 'FORM', 'Tablet', 'string'),
  ('MED006', 'DOSAGE', '500mg', 'string'),
  ('MED006', 'FORM', 'Tablet', 'string'),
  ('MED007', 'DOSAGE', '20mg', 'string'),
  ('MED007', 'FORM', 'Tablet', 'string'),
  ('MED008', 'DOSAGE', '50mcg', 'string'),
  ('MED008', 'FORM', 'Tablet', 'string'),
  ('MED009', 'DOSAGE', '20mg', 'string'),
  ('MED009', 'FORM', 'Capsule', 'string'),
  ('MED010', 'DOSAGE', '40mg', 'string'),
  ('MED010', 'FORM', 'Tablet', 'string');
EOF

# Create the second SQLite file with sample data
sqlite3 sample1.sqlite <<EOF
CREATE TABLE CodeSystems (
  name TEXT UNIQUE,
  url TEXT
);
INSERT INTO CodeSystems (name, url) VALUES ('ex1', 'http://ex1.example.org');


CREATE TABLE Concepts (
  id INTEGER PRIMARY KEY,
  code TEXT UNIQUE,
  display TEXT
);

INSERT INTO Concepts (code, display) VALUES
  ('CONCEPT1', 'Sample Concept 1'),
  ('CONCEPT2', 'Sample Concept 2'),
  ('CONCEPT3', 'Sample Concept 3');
EOF

# Create the third SQLite file with sample data
sqlite3 sample2.sqlite <<EOF
CREATE TABLE CodeSystems (
  name TEXT UNIQUE,
  url TEXT
);
INSERT INTO CodeSystems (name, url) VALUES ('ex2', 'http://ex2.example.org');


CREATE TABLE Concepts (
  id INTEGER PRIMARY KEY,
  code TEXT UNIQUE,
  display TEXT
);

INSERT INTO Concepts (code, display) VALUES
  ('CONCEPT4', 'Another Concept 4'),
  ('CONCEPT5', 'Another Concept 5');
EOF
