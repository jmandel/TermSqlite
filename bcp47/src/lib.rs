wit_bindgen::generate!({
    world: "fhir-terminology-engine",
    additional_derives: [Clone, PartialEq, Eq, Ord, PartialOrd],

});

struct MyHost;

impl Guest for MyHost {
    #[doc = " Parses a code into its full concept representation, optionally restricting the set of properties requested"]
    fn parse(
        code: _rt::String,
        properties: Option<_rt::Vec<_rt::String>>,
    ) -> data_types::ParseResult {
        let code = "en-US-u-co-phonebk-x-private".to_string();
        let result = parse(code.clone(), None, &HostTerminology);
        todo!()
    }

    #[doc = " Checks if one concept is a descendant of another concept"]
    fn subsumes(ancestor: _rt::String, descendant: _rt::String) -> bool {
        todo!()
    }
}

export!(MyHost);
pub trait TerminologyDbLookup {
    fn lookup(&self, code: &str, properties: Option<&[String]>) -> Option<Concept>;
}

struct HostTerminology;
impl TerminologyDbLookup for HostTerminology {
    fn lookup(&self, code: &str, properties: Option<&[String]>) -> Option<Concept> {
        terminology_db::lookup(code, properties)
    }
}
static terminology_db: HostTerminology = HostTerminology;

mod mock_terminology_db {
    use self::fhirtx::spec::terminology_db::Concept;

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

    impl TerminologyDbLookup for MockTerminologyDb {
        fn lookup(&self, code: &str, _properties: Option<&[String]>) -> Option<Concept> {
            self.concepts.iter().find(|c| c.code == code).cloned()
        }
    }
}

use std::iter::Peekable;
use std::str::Split;

use exports::fhirtx::spec::terminology_engine::Guest;
use fhirtx::spec::data_types::{
    self, Concept, ParseDetail, ParseResult, Property, Severity, ValueX,
};
use fhirtx::spec::terminology_db;


#[derive(Debug, PartialEq, Clone)]
pub struct LanguageTag {
    pub language: String,
    pub extlang: Vec<String>,
    pub script: Option<String>,
    pub region: Option<String>,
    pub variants: Vec<String>,
    pub extensions: Vec<Extension>,
    pub private_use: Option<String>,
}

#[derive(Debug, PartialEq, Clone)]
pub struct Extension {
    pub singleton: char,
    pub parts: Vec<String>,
}

struct Parser<'a, T: TerminologyDbLookup> {
    parts: Peekable<Split<'a, char>>,
    code: &'a str,
    db: &'a T,
    language_tag: LanguageTag,
}

impl<'a, T: TerminologyDbLookup> Parser<'a, T> {
    fn new(input: &'a str, db: &'a T) -> Self {
        Parser {
            parts: input.split('-').peekable(),
            code: input,
            db,
            language_tag: LanguageTag {
                language: String::new(),
                extlang: Vec::new(),
                script: None,
                region: None,
                variants: Vec::new(),
                extensions: Vec::new(),
                private_use: None,
            },
        }
    }

    fn parse(&mut self) -> ParseResult {
        let mut details = Vec::new();

        self.parse_language(&mut details);
        self.parse_extlang(&mut details);
        self.parse_extlang(&mut details);
        self.parse_extlang(&mut details);
        self.parse_script(&mut details);
        self.parse_region(&mut details);
        self.parse_variants(&mut details);
        self.parse_extensions(&mut details);
        self.parse_private_use(&mut details);

        details.sort();
        details.dedup();

        if !details.is_empty() {
            return ParseResult {
                details,
                concept: None,
            };
        }

        ParseResult {
            details,
            concept: Some(self.language_tag_to_concept()),
        }
    }

    fn parse_language(&mut self, details: &mut Vec<ParseDetail>) {
        if let Some(language) = self.parts.next() {
            if language.len() >= 2
                && language.len() <= 3
                && language.chars().all(char::is_alphabetic)
            {
                if let Some(language_concept) = self.db.lookup(language, None) {
                    self.language_tag.language = language.to_string();
                } else {
                    details.push(ParseDetail {
                        severity: Severity::Error,
                        key: "language".to_string(),
                        value: ValueX::ValueString(format!("Invalid language code: {}", language)),
                    });
                }
            } else if language.len() == 4 {
                // Reserved for future use
                // do nothing
            } else if language.len() >= 5 && language.len() <= 8 {
                // Registered language subtag
                if let Some(language_concept) = self.db.lookup(language, None) {
                    self.language_tag.language = language.to_string();
                } else {
                    details.push(ParseDetail {
                        severity: Severity::Error,
                        key: "language".to_string(),
                        value: ValueX::ValueString(format!("Invalid language code: {}", language)),
                    });
                }
            } else {
                details.push(ParseDetail {
                    severity: Severity::Error,
                    key: "language".to_string(),
                    value: ValueX::ValueString(format!("Invalid language code: {}", language)),
                });
            }
        } else {
            details.push(ParseDetail {
                severity: Severity::Error,
                key: "language".to_string(),
                value: ValueX::ValueString("Empty language tag".to_string()),
            });
        }
    }

    fn parse_extlang(&mut self, details: &mut Vec<ParseDetail>) {
        if let Some(extlang) = self.parts.peek().cloned() {
            if extlang.len() == 3 && extlang.chars().all(char::is_alphabetic) {
                let extlang_str = extlang.to_string();
                self.parts.next();
                if let Some(extlang_concept) = self.db.lookup(&extlang_str, None) {
                    self.language_tag.extlang.push(extlang_str);
                } else {
                    details.push(ParseDetail {
                        severity: Severity::Error,
                        key: "extLang".to_string(),
                        value: ValueX::ValueString(format!("Invalid extLang code: {}", extlang)),
                    });
                }
            }
        }
    }

    fn parse_script(&mut self, details: &mut Vec<ParseDetail>) {
        if let Some(script) = self.parts.peek() {
            if script.len() == 4 {
                let script_str = script.to_string();
                if let Some(script_concept) = self.db.lookup(&script_str, None) {
                    self.parts.next();
                    self.language_tag.script = Some(script_str);
                } else {
                    details.push(ParseDetail {
                        severity: Severity::Error,
                        key: "script".to_string(),
                        value: ValueX::ValueString(format!("Invalid script code: {}", script)),
                    });
                }
            }
        }
    }

    fn parse_region(&mut self, details: &mut Vec<ParseDetail>) {
        if let Some(region) = self.parts.peek() {
            if (region.len() == 2 && region.chars().all(char::is_alphabetic))
                || (region.len() == 3 && region.chars().all(char::is_numeric))
            {
                let region_str = region.to_string();
                if let Some(region_concept) = self.db.lookup(&region_str, None) {
                    self.parts.next();
                    self.language_tag.region = Some(region_str);
                } else {
                    details.push(ParseDetail {
                        severity: Severity::Error,
                        key: "region".to_string(),
                        value: ValueX::ValueString(format!("Invalid region code: {}", region)),
                    });
                }
            }
        }
    }

    fn parse_variants(&mut self, details: &mut Vec<ParseDetail>) {
        while let Some(variant) = self.parts.peek() {
            if (variant.len() >= 5
                && variant.len() <= 8
                && variant.chars().next().unwrap().is_alphanumeric())
                || (variant.len() == 4 && variant.chars().next().unwrap().is_numeric())
            {
                let variant_str = variant.to_string();
                if let Some(variant_concept) = self.db.lookup(&variant_str, None) {
                    self.parts.next();
                    self.language_tag.variants.push(variant_str);
                } else {
                    details.push(ParseDetail {
                        severity: Severity::Error,
                        key: "variant".to_string(),
                        value: ValueX::ValueString(format!("Invalid variant code: {}", variant)),
                    });
                }
            } else {
                break;
            }
        }
    }

    fn parse_extensions(&mut self, details: &mut Vec<ParseDetail>) {
        if let Some(ext_str) = self.parts.peek().cloned() {
            if ext_str.len() == 1 && ext_str != "x" {
                let singleton = ext_str.chars().next().unwrap();
                self.parts.next(); // Consume the singleton

                let mut ext_parts = Vec::new();
                while let Some(part) = self.parts.peek().cloned() {
                    if part.len() >= 2 && part.len() <= 8 {
                        ext_parts.push(part.to_string());
                        self.parts.next(); // Consume the extension part
                    } else {
                        break;
                    }
                }

                if !ext_parts.is_empty() {
                    self.language_tag.extensions.push(Extension {
                        singleton,
                        parts: ext_parts,
                    });
                } else {
                    details.push(ParseDetail {
                        severity: Severity::Error,
                        key: "extension".to_string(),
                        value: ValueX::ValueString(format!("Invalid extension: {}", ext_str)),
                    });
                }
            }
        }
    }

    fn parse_private_use(&mut self, details: &mut Vec<ParseDetail>) {
        if let Some(x) = self.parts.peek() {
            if *x == "x" {
                self.parts.next();
                let mut private_use_parts = Vec::new();
                while let Some(private_use_part) = self.parts.next() {
                    if private_use_part.len() >= 1 && private_use_part.len() <= 8 {
                        private_use_parts.push(private_use_part.to_string());
                    } else {
                        break;
                    }
                }
                if !private_use_parts.is_empty() {
                    self.language_tag.private_use = Some(private_use_parts.join("-"));
                } else {
                    details.push(ParseDetail {
                        severity: Severity::Error,
                        key: "privateUse".to_string(),
                        value: ValueX::ValueString("Invalid private use".to_string()),
                    });
                }
            }
        }
    }
    fn language_tag_to_concept(&self) -> Concept {
        let mut properties = Vec::new();

        properties.push(Property {
            code: "language".to_string(),
            value: ValueX::ValueString(self.language_tag.language.clone()),
        });

        for extlang in &self.language_tag.extlang {
            properties.push(Property {
                code: "extLang".to_string(),
                value: ValueX::ValueString(extlang.clone()),
            });
        }

        if let Some(script) = &self.language_tag.script {
            properties.push(Property {
                code: "script".to_string(),
                value: ValueX::ValueString(script.clone()),
            });
        }

        if let Some(region) = &self.language_tag.region {
            properties.push(Property {
                code: "region".to_string(),
                value: ValueX::ValueString(region.clone()),
            });
        }

        for variant in &self.language_tag.variants {
            properties.push(Property {
                code: "variant".to_string(),
                value: ValueX::ValueString(variant.clone()),
            });
        }

        for extension in &self.language_tag.extensions {
            let ext_str = format!("{}-{}", extension.singleton, extension.parts.join("-"));
            properties.push(Property {
                code: "extension".to_string(),
                value: ValueX::ValueString(ext_str),
            });
        }

        if let Some(private_use) = &self.language_tag.private_use {
            properties.push(Property {
                code: "privateUse".to_string(),
                value: ValueX::ValueString(private_use.clone()),
            });
        }

        Concept {
            code: self.code.to_string(),
            properties,
        }
    }
}

fn parse<T: TerminologyDbLookup>(
    code: String,
    properties: Option<Vec<String>>,
    db: &T,
) -> ParseResult {
    let mut parser = Parser::new(&code, db);
    parser.parse()
}

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
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn assert_parse_result(result: ParseResult, expected: ParseResult) {
        assert_eq!(result.concept, expected.concept);
        assert_eq!(result.details.len(), expected.details.len());
        for (actual_detail, expected_detail) in result.details.iter().zip(expected.details.iter()) {
            assert_eq!(actual_detail.severity, expected_detail.severity);
            assert_eq!(actual_detail.key, expected_detail.key);
            assert_eq!(actual_detail.value, expected_detail.value);
        }
    }

    fn create_expected_result(
        code: &str,
        property_types: Vec<(&str, &str)>,
        details: Vec<ParseDetail>,
    ) -> ParseResult {
        let mut properties = Vec::new();
        for (pn, pv) in property_types {
            properties.push(Property {
                code: pn.to_string(),
                value: ValueX::ValueString(pv.to_string()),
            });
        }
        ParseResult {
            details,
            concept: Some(Concept {
                code: code.to_string(),
                properties,
            }),
        }
    }

    #[test]
    fn test_parse_simple_language_code() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));

        let code = "en".to_string();
        let result = parse(code.clone(), None, &db);
        let expected = create_expected_result(&code, vec![("language", "en")], vec![]);
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_language_with_script() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("zh", "language", Some("Chinese")));
        db.insert(create_concept("Hant", "script", Some("Traditional")));

        let code = "zh-Hant".to_string();
        let result = parse(code.clone(), None, &db);
        let expected =
            create_expected_result(&code, vec![("language", "zh"), ("script", "Hant")], vec![]);
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_language_with_region() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        db.insert(create_concept("US", "region", Some("United States")));

        let code = "en-US".to_string();
        let result = parse(code.clone(), None, &db);
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

        let code = "sl-IT-nedis-rozaj".to_string();
        let result = parse(code.clone(), None, &db);
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

        let code = "en-US-u-co-phonebk".to_string();
        let result = parse(code.clone(), None, &db);
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

        let code = "en-x-shhabc".to_string();
        let result = parse(code.clone(), None, &db);
        let expected = create_expected_result(
            &code,
            vec![("language", "en"), ("privateUse", "shhabc")],
            vec![],
        );
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_with_invalid_language() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();

        let code = "invalid".to_string();
        let result = parse(code.clone(), None, &db);
        let expected = ParseResult {
            details: vec![ParseDetail {
                severity: Severity::Error,
                key: "language".to_string(),
                value: ValueX::ValueString("Invalid language code: invalid".to_string()),
            }],
            concept: None,
        };
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_with_invalid_extlang() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));

        let code = "en-abc".to_string();
        let result = parse(code.clone(), None, &db);
        let expected = ParseResult {
            details: vec![ParseDetail {
                severity: Severity::Error,
                key: "extLang".to_string(),
                value: ValueX::ValueString("Invalid extLang code: abc".to_string()),
            }],
            concept: None,
        };
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_with_multiple_extensions() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        db.insert(create_concept("US", "region", Some("United States")));

        let code = "en-US-u-co-phonebk-x-priv".to_string();
        let result = parse(code.clone(), None, &db);
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

        let code = "en-US-u-co-phonebk-x-private".to_string();
        let result = parse(code.clone(), None, &db);
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

        let code = "en-US-u".to_string();
        let result = parse(code.clone(), None, &db);
        let expected = ParseResult {
            details: vec![ParseDetail {
                severity: Severity::Error,
                key: "extension".to_string(),
                value: ValueX::ValueString("Invalid extension: u".to_string()),
            }],
            concept: None,
        };
        assert_parse_result(result, expected);
    }
}
