pub mod codesystem;
mod extism;
use extism::*;
use regex::Regex;

#[derive(Debug, PartialEq, Clone)]
pub struct LanguageTag {
    pub language: String,
    pub extlang: Vec<String>,
    pub script: Option<String>,
    pub region: Option<String>,
    pub variants: Vec<String>,
    pub extensions: Vec<Extension>,
    pub private_use: Vec<String>,
}

#[derive(Debug, PartialEq, Clone)]
pub struct Extension {
    pub singleton: char,
    pub parts: Vec<String>,
}

type CodeWithDoc<'a> = (String, &'a str, Option<Severity>);
impl LanguageTag {
    fn properties(&self) -> Vec<CodeWithDoc> {
        vec![(self.language.clone(), "language", Some(Severity::Error))]
            .into_iter()
            .chain(
                self.extlang
                    .iter()
                    .map(|c| (c.clone(), "extlang", Some(Severity::Warning))),
            )
            .chain(
                self.script
                    .iter()
                    .map(|c| (c.clone(), "script", Some(Severity::Warning))),
            )
            .chain(
                self.region
                    .iter()
                    .map(|c| (c.clone(), "region", Some(Severity::Warning))),
            )
            .chain(
                self.variants
                    .iter()
                    .map(|c| (c.clone(), "variant", Some(Severity::Warning))),
            )
            .chain(self.extensions.iter().map(|e| {
                (
                    format!("{}-{}", e.singleton, e.parts.join("-")),
                    "extension",
                    None,
                )
            }))
            .chain(
                self.private_use
                    .iter()
                    .map(|c| (c.clone(), "privateuse", None)),
            )
            .collect()
    }

    fn into_concept(
        &self,
        code: &str,
        db: &dyn TerminologyDb,
    ) -> (Option<Concept>, Vec<ParseDetail>) {
        let mut language_display = None;
        let mut region_display = None;
        let mut script_display = None;
        let mut properties = Vec::new();
        let mut parse_details = Vec::new();

        for (c, t, sev) in self.properties() {
            if let Some(severity) = sev {
                let lookup_code = format!("{}-{}", t, c);
                let lookup_result = db.db_lookup(LookupRequest {
                    code: lookup_code.clone(),
                    properties: None,
                });

                match lookup_result.concept {
                    Some(concept) => {
                        let display = concept.display.unwrap_or_else(|| c.clone());
                        properties.push(Property {
                            code: t.to_string(),
                            value: ValueX::ValueString(display.clone()),
                        });

                        match t {
                            "language" => language_display = Some(display),
                            "region" => region_display = Some(display),
                            "script" => script_display = Some(display),
                            _ => {}
                        }
                    }
                    None => {
                        properties.push(Property {
                            code: t.to_string(),
                            value: ValueX::ValueString(c.clone()),
                        });

                        parse_details.push(ParseDetail {
                            key: t.to_string(),
                            severity,
                            value: ValueX::ValueString(format!("Invalid {} subtag: {}", t, c)),
                        });
                    }
                }
            }
        }

        let parts = [
            region_display.map(|region| format!("Region: {}", region)),
            script_display.map(|script| format!("Script: {}", script)),
            match self.variants.len() {
                0 => None,
                _ => Some(format!("Variant: {}", self.variants.join(", "))),
            },
        ]
        .iter()
        .flatten()
        .cloned()
        .collect::<Vec<_>>()
        .join(", ");

        let display = match language_display {
            Some(language) => {
                if !parts.is_empty() {
                    format!("Language: {} ({})", language, parts)
                } else {
                    format!("Language: {}", language)
                }
            }
            None => format!("Language tag: {}", code),
        };
        (
            Some(Concept {
                code: code.to_string(),
                properties,
                display: Some(display),
                ..Default::default()
            }),
            parse_details,
        )
    }
}

impl From<&str> for LookupRequest {
    fn from(code: &str) -> Self {
        LookupRequest {
            code: code.to_string(),
            properties: None,
        }
    }
}

impl dyn TerminologyDb + '_ {
    #[allow(dead_code)]
    fn lookup_display(&self, code: &str) -> String {
        self.db_lookup(code.into())
            .concept
            .and_then(|c| {
                c.properties
                    .iter()
                    .filter(|p| p.code == "display")
                    .filter_map(|p| match &p.value {
                        ValueX::ValueString(s) => Some(s.clone()),
                        _ => None,
                    })
                    .next()
            })
            .unwrap_or(code.to_string())
    }
}

impl<T> TerminologyEngine<T> for Guest<T>
where
    T: TerminologyDb,
{
    fn parse(&self, request: ParseRequest) -> ParseResponse {
        self.parse_language_tag(&request.code)
            .map(|tag| {
                let (concept, details) = tag.into_concept(&request.code, &self.db);
                ParseResponse { concept, details }
            })
            .unwrap_or_else(|detail| ParseResponse {
                concept: None,
                details: vec![detail],
            })
    }

    fn subsumes(&self, _req: SubsumesRequest) -> SubsumesResponse {
        todo!()
    }

    fn metadata(&self) -> String {
        codesystem::CODE_SYSTEM.to_string()
    }
}

impl<T> Guest<T>
where
    T: TerminologyDb,
{
    fn parse_language_tag(&self, input: &str) -> Result<LanguageTag, ParseDetail> {
        let re = Regex::new(r"-").unwrap();
        let parts: Vec<&str> = re.split(input).collect();

        let language: String;
        let mut extlang = Vec::new();
        let mut script = None;
        let mut region = None;
        let mut variants = Vec::new();
        let mut extensions = Vec::new();
        let mut private_use = Vec::new();

        let mut i = 0;

        // Language
        if i < parts.len()
            && parts[i].len() >= 2
            && parts[i].len() <= 8
            && parts[i].chars().all(|c| c.is_ascii_alphanumeric())
        {
            language = parts[i].to_string();
            i += 1;
        } else {
            return Err(ParseDetail {
                key: "language".to_string(),
                severity: Severity::Error,
                value: ValueX::ValueString(format!("Invalid language subtag: {}", parts[i])),
            });
        }

        // Extlang
        while i < parts.len()
            && parts[i].len() == 3
            && parts[i].chars().all(|c| c.is_ascii_alphabetic())
            && extlang.len() < 3
        {
            extlang.push(parts[i].to_string());
            i += 1;
        }

        // Script
        if i < parts.len()
            && parts[i].len() == 4
            && parts[i].chars().all(|c| c.is_ascii_alphabetic())
        {
            script = Some(parts[i].to_string());
            i += 1;
        }

        // Region
        if i < parts.len()
            && ((parts[i].len() == 2 && parts[i].chars().all(|c| c.is_ascii_alphabetic()))
                || (parts[i].len() == 3 && parts[i].chars().all(|c| c.is_ascii_digit())))
        {
            region = Some(parts[i].to_string());
            i += 1;
        }

        // Variants
        while i < parts.len()
            && ((parts[i].len() >= 5
                && parts[i].len() <= 8
                && parts[i].chars().all(|c| c.is_ascii_alphanumeric()))
                || (parts[i].len() == 4 && parts[i].chars().next().unwrap().is_ascii_digit()))
            && variants.len() < 5
        {
            variants.push(parts[i].to_string());
            i += 1;
        }

        // Extensions
        while i < parts.len()
            && parts[i].len() == 1
            && parts[i].chars().next().unwrap().is_ascii_alphabetic()
            && parts[i] != "x"
        {
            let singleton = parts[i].chars().next().unwrap();
            let mut extension_parts = Vec::new();
            i += 1;
            while i < parts.len()
                && parts[i].len() >= 2
                && parts[i].len() <= 8
                && parts[i].chars().all(|c| c.is_ascii_alphanumeric())
            {
                extension_parts.push(parts[i].to_string());
                i += 1;
            }
            if !extension_parts.is_empty() {
                extensions.push(Extension {
                    singleton,
                    parts: extension_parts,
                });
            } else {
                return Err(ParseDetail {
                    key: "extension".to_string(),
                    severity: Severity::Error,
                    value: ValueX::ValueString(format!(
                        "Invalid extension subtag: {}",
                        parts[i - 1]
                    )),
                });
            }
        }

        // Private Use
        if i < parts.len() && parts[i] == "x" {
            i += 1;
            private_use = Vec::new();
            while i < parts.len() {
                if parts[i].len() >= 1
                    && parts[i].len() <= 8
                    && parts[i].chars().all(|c| c.is_ascii_alphanumeric())
                {
                    private_use.push(parts[i].to_string());
                    i += 1;
                } else {
                    return Err(ParseDetail {
                        key: "privateUse".to_string(),
                        severity: Severity::Error,
                        value: ValueX::ValueString(format!(
                            "Invalid private use subtag: {}",
                            parts[i]
                        )),
                    });
                }
            }
        }

        if i < parts.len() {
            return Err(ParseDetail {
                key: "language".to_string(),
                severity: Severity::Error,
                value: ValueX::ValueString(format!("Invalid language tag: {}", input)),
            });
        }

        Ok(LanguageTag {
            language,
            extlang,
            script,
            region,
            variants,
            extensions,
            private_use,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn create_concept(code: &str, property_type: &str, display: Option<&str>) -> Concept {
        let mut properties = vec![Property {
            code: "type".to_string(),
            value: ValueX::ValueString(property_type.to_string()),
        }];
        if let Some(display) = display {
            properties.push(Property {
                code: "display".to_string(),
                value: ValueX::ValueString(display.to_string()),
            });
        }
        Concept {
            code: code.to_string(),
            properties,
            ..Default::default()
        }
    }
    fn assert_parse_result(result: ParseResponse, expected: ParseResponse) {
        println!("{:#?}", result);
        assert_eq!(result.concept, expected.concept);
        assert_eq!(result.details.len(), expected.details.len());
        for (actual_detail, expected_detail) in result.details.iter().zip(expected.details.iter()) {
            assert_eq!(actual_detail.severity, expected_detail.severity);
            assert_eq!(actual_detail.key, expected_detail.key);
            // assert_eq!(actual_detail.value, expected_detail.value);
        }
    }

    fn create_expected_result(
        code: &str,
        property_types: Vec<(&str, &str)>,
        details: Vec<ParseDetail>,
    ) -> ParseResponse {
        let mut properties = Vec::new();
        for (pn, pv) in property_types {
            properties.push(Property {
                code: pn.to_string(),
                value: ValueX::ValueString(pv.to_string()),
            });
        }
        ParseResponse {
            details,
            concept: Some(Concept {
                code: code.to_string(),
                properties,
                ..Default::default()
            }),
        }
    }

    #[test]
    fn test_parse_simple_language_code() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        let parser = Guest::new(db);

        let code = "en".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        let expected = create_expected_result(&code, vec![("language", "en")], vec![]);
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_language_with_script() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("zh", "language", Some("Chinese")));
        db.insert(create_concept("Hant", "script", Some("Traditional")));
        let parser = Guest::new(db);

        let code = "zh-Hant".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        let expected =
            create_expected_result(&code, vec![("language", "zh"), ("script", "Hant")], vec![]);
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_language_with_region() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        db.insert(create_concept("US", "region", Some("United States")));
        let parser = Guest::new(db);

        let code = "en-US".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        let expected =
            create_expected_result(&code, vec![("language", "en"), ("region", "US")], vec![]);
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_multiple_variants() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("sl", "language", Some("Slovenian")));
        db.insert(create_concept("IT", "region", Some("Italy")));
        db.insert(create_concept("nedis", "variant", Some("Nadiza dialect")));
        db.insert(create_concept("rozaj", "variant", Some("Resian dialect")));
        let parser = Guest::new(db);

        let code = "sl-IT-nedis-rozaj".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        let expected = create_expected_result(
            &code,
            vec![
                ("language", "sl"),
                ("region", "IT"),
                ("variant", "nedis"),
                ("variant", "rozaj"),
            ],
            vec![],
        );
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_with_extension() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        db.insert(create_concept("US", "region", Some("United States")));
        let parser = Guest::new(db);

        let code = "en-US-u-co-phonebk".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        let expected = create_expected_result(
            &code,
            vec![
                ("language", "en"),
                ("region", "US"),
                ("extension", "u-co-phonebk"),
            ],
            vec![],
        );
        assert_parse_result(result, expected);
    }
    #[test]
    fn test_parse_with_private_use() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        db.insert(create_concept("US", "region", Some("United States")));
        let parser = Guest::new(db);

        let code = "en-x-shhabc".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        let expected = create_expected_result(
            &code,
            vec![("language", "en"), ("privateUse", "shhabc")],
            vec![],
        );
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_with_invalid_language() {
        let db = mock_terminology_db::MockTerminologyDb::new();
        let parser = Guest::new(db);

        let code = "invalid".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        let expected = create_expected_result(
            &code,
            vec![("language", "invalid")],
            vec![ParseDetail {
                severity: Severity::Error,
                key: "language".to_string(),
                value: ValueX::ValueString("Invalid language code: invalid".to_string()),
            }],
        );
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_with_invalid_extlang() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        let parser = Guest::new(db);

        let code = "en-abc".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        println!("{:#?}", result);
        assert!(result.concept.is_some());
        assert!(result
            .details
            .iter()
            .any(|d| d.severity == Severity::Warning && d.key == "extLang"));
    }

    #[test]
    fn test_parse_with_multiple_extensions() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        db.insert(create_concept("US", "region", Some("United States")));
        let parser = Guest::new(db);

        let code = "en-US-u-co-phonebk-x-priv".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        let expected = create_expected_result(
            &code,
            vec![
                ("language", "en"),
                ("region", "US"),
                ("extension", "u-co-phonebk"),
                ("privateUse", "priv"),
            ],
            vec![],
        );
        assert_parse_result(result, expected);
    }
    #[test]
    fn test_parse_with_extension_and_private_use() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        db.insert(create_concept("US", "region", Some("United States")));
        db.insert(create_concept("u", "extension", Some("Extension")));
        db.insert(create_concept("co", "extension", Some("Collation")));
        db.insert(create_concept(
            "phonebk",
            "extension",
            Some("Phonebook sort order"),
        ));
        let parser = Guest::new(db);

        let code = "en-US-u-co-phonebk-x-private".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        let expected = create_expected_result(
            &code,
            vec![
                ("language", "en"),
                ("region", "US"),
                ("extension", "u-co-phonebk"),
                ("privateUse", "private"),
            ],
            vec![],
        );
        assert_parse_result(result, expected);
    }
    #[test]
    fn test_parse_with_invalid_extension() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        db.insert(create_concept("US", "region", Some("United States")));
        let parser = Guest::new(db);

        let code = "en-US-u-be-abcdefghi".to_string();
        let result = parser.parse(ParseRequest {
            code: code.clone(),
            properties: None,
        });
        assert!(result.concept.is_none());
    }
    mod mock_terminology_db {

        use super::*;
        pub struct MockTerminologyDb {
            concepts: Vec<Concept>,
        }

        impl MockTerminologyDb {
            pub fn new() -> Self {
                MockTerminologyDb {
                    concepts: Vec::new(),
                }
            }

            pub fn insert(&mut self, concept: Concept) {
                self.concepts.push(concept);
            }
        }

        impl TerminologyDb for MockTerminologyDb {
            fn db_lookup(&self, req: LookupRequest) -> LookupResponse {
                LookupResponse {
                    concept: self.concepts.iter().find(|c| c.code == req.code).cloned(),
                }
            }

            fn db_subsumes(&self, _req: SubsumesRequest) -> SubsumesResponse {
                todo!()
            }
        }
    }
}
