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
        let mut parser = Parser::new(&HostTerminology);
        let result = parser.parse(code.clone(), None);
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
    pub private_use: Option<Vec<String>>,
}

#[derive(Debug, PartialEq)]
enum BCP47Error {
    ExtensionHasInvalidLength,
    // Add more error variants as needed
}

impl LanguageTag {
    fn into_concept(self, code: &str) -> Concept {
        let mut properties = Vec::new();

        properties.push(Property {
            code: "language".to_string(),
            value: ValueX::ValueString(self.language),
        });

        for extlang in self.extlang {
            properties.push(Property {
                code: "extlang".to_string(),
                value: ValueX::ValueString(extlang),
            });
        }

        if let Some(script) = self.script {
            properties.push(Property {
                code: "script".to_string(),
                value: ValueX::ValueString(script),
            });
        }

        if let Some(region) = self.region {
            properties.push(Property {
                code: "region".to_string(),
                value: ValueX::ValueString(region),
            });
        }

        for variant in self.variants {
            properties.push(Property {
                code: "variant".to_string(),
                value: ValueX::ValueString(variant),
            });
        }

        for extension in self.extensions {
            let ext_str = format!("{}-{}", extension.singleton, extension.parts.join("-"));
            properties.push(Property {
                code: "extension".to_string(),
                value: ValueX::ValueString(ext_str),
            });
        }

        for pu in self.private_use.unwrap_or(vec![]) {
            properties.push(Property {
                code: "privateUse".to_string(),
                value: ValueX::ValueString(pu),
            });
        }

        Concept {
            code: code.to_string(),
            properties,
        }
    }
}

#[derive(Debug, PartialEq, Clone)]
pub struct Extension {
    pub singleton: char,
    pub parts: Vec<String>,
}

use nom::bytes::streaming::take_while;
use nom::character::complete::alphanumeric1;
use nom::combinator::{eof, peek, verify};
use nom::error::{context, ContextError, Error};
use nom::multi::{many1, many_m_n};
use nom::sequence::terminated;
use nom::{
    branch::alt,
    bytes::complete::{tag, take_while_m_n},
    combinator::{map, opt},
    multi::many0,
    sequence::{preceded, tuple},
    IResult,
};

struct Parser<'a, T: TerminologyDbLookup> {
    db: &'a T,
}

impl<'a, T: TerminologyDbLookup> Parser<'a, T> {
    fn new(db: &'a T) -> Self {
        Parser { db }
    }

    fn parse_language<'b>(&'a self, input: &'b str) -> IResult<&'b str, String> {
        map(
            terminated(
                alt((
                    take_while_m_n(2, 3, |c: char| c.is_ascii_alphabetic()),
                    take_while_m_n(5, 8, |c: char| c.is_ascii_alphanumeric()),
                )),
                peek(alt((tag("-"), nom::combinator::eof))),
            ),
            |s: &str| s.to_string(),
        )(input)
    }

    fn parse_extlang<'b>(&'a self, input: &'b str) -> IResult<&'b str, String> {
        map(
            preceded(
                tag::<_, &'b str, _>("-"),
                terminated(
                    take_while_m_n(3, 3, |c: char| c.is_ascii_alphabetic()),
                    peek(alt((tag("-"), nom::combinator::eof))),
                ),
            ),
            |extlang| extlang.to_string(),
        )(input)
    }

    fn parse_script<'b>(&'a self, input: &'b str) -> IResult<&'b str, String> {
        preceded(
            tag("-"),
            terminated(
                map(
                    take_while_m_n(4, 4, |c: char| c.is_ascii_alphabetic()),
                    |s: &str| s.to_string(),
                ),
                peek(alt((tag("-"), nom::combinator::eof))),
            ),
        )(input)
    }

    fn parse_region<'b>(&'a self, input: &'b str) -> IResult<&'b str, String> {
        preceded(
            tag("-"),
            terminated(
                alt((
                    map(
                        take_while_m_n(2, 2, |c: char| c.is_ascii_alphabetic()),
                        |s: &str| s.to_string(),
                    ),
                    map(
                        take_while_m_n(3, 3, |c: char| c.is_ascii_digit()),
                        |s: &str| s.to_string(),
                    ),
                )),
                peek(alt((tag("-"), nom::combinator::eof))),
            ),
        )(input)
    }

    fn parse_variant<'b>(&'a self, input: &'b str) -> IResult<&'b str, String> {
        preceded(
            tag("-"),
            terminated(
                alt((
                    map(
                        take_while_m_n(5, 8, |c: char| c.is_ascii_alphanumeric()),
                        |s: &str| s.to_string(),
                    ),
                    map(
                        take_while_m_n(4, 4, |c: char| c.is_ascii_digit()),
                        |s: &str| s.to_string(),
                    ),
                )),
                peek(alt((tag("-"), nom::combinator::eof))),
            ),
        )(input)
    }

    fn parse_extension<'b, E>(&self, input: &'b str) -> IResult<&'b str, Extension, E>
    where
        E: nom::error::ParseError<&'b str> + ContextError<&'b str>,
    {
        context(
            "extension",
            map(
                preceded(
                    tag::<_, _, E>("-"),
                    tuple((
                        take_while_m_n(1, 1, |c: char| c != 'x' && c.is_ascii_alphanumeric()),
                        many1(preceded(
                            tag("-"),
                            verify(
                                take_while(|c: char| c.is_ascii_alphanumeric()),
                                |s: &str| s.len() >= 2 && s.len() <= 8,
                            ),
                        )),
                    )),
                ),
                |(singleton, parts)| Extension {
                    singleton: singleton.chars().next().unwrap(),
                    parts: parts.into_iter().map(|s: &str| s.to_string()).collect(),
                },
            ),
        )(input)
    }

    fn parse_private_use<'b>(&'a self, input: &'b str) -> IResult<&'b str, Vec<String>> {
        preceded(
            tag("-x-"),
            many1(terminated(
                map(alphanumeric1, |s: &str| s.to_string()),
                peek(alt((tag("-"), eof))),
            )),
        )(input)
    }

    fn parse_language_tag<'b>(&'a self, input: &'b str) -> IResult<&'b str, LanguageTag> {
        map(
            terminated(
                tuple((
                    |i| self.parse_language(i),
                    many_m_n(0, 3, |i| self.parse_extlang(i)),
                    opt(|i| self.parse_script(i)),
                    opt(|i| self.parse_region(i)),
                    many0(|i| self.parse_variant(i)),
                    many0(|i| self.parse_extension(i)),
                    opt(|i| self.parse_private_use(i)),
                )),
                eof,
            ),
            |(language, extlang, script, region, variants, extensions, private_use)| LanguageTag {
                language,
                extlang,
                script,
                region,
                variants,
                extensions,
                private_use,
            },
        )(input)
    }

    fn validate_tag(&self, tag: &LanguageTag) -> Vec<ParseDetail> {
        let mut errors = Vec::new();

        if self.db.lookup(&tag.language, None).is_none() {
            errors.push(ParseDetail {
                severity: Severity::Error,
                key: "language".to_string(),
                value: ValueX::ValueString(format!("Invalid language code: {}", tag.language)),
            });
        }

        for ext in &tag.extlang {
            if self.db.lookup(ext, None).is_none() {
                errors.push(ParseDetail {
                    severity: Severity::Error,
                    key: "extLang".to_string(),
                    value: ValueX::ValueString(format!("Invalid extLang code: {}", ext)),
                });
            }
        }

        if let Some(script) = &tag.script {
            if self.db.lookup(script, None).is_none() {
                errors.push(ParseDetail {
                    severity: Severity::Error,
                    key: "script".to_string(),
                    value: ValueX::ValueString(format!("Invalid script code: {}", script)),
                });
            }
        }

        errors
    }

    fn parse(&self, code: String, _properties: Option<Vec<String>>) -> ParseResult {
        match self.parse_language_tag(&code) {
            Ok((_, language_tag)) => {
                let errors = self.validate_tag(&language_tag);
                if errors.is_empty() {
                    let concept = language_tag.into_concept(&code);
                    ParseResult {
                        details: Vec::new(),
                        concept: Some(concept),
                    }
                } else {
                    ParseResult {
                        details: errors,
                        concept: None,
                    }
                }
            }
            Err(v) => ParseResult {
                details: vec![ParseDetail {
                    severity: Severity::Error,
                    key: "language".to_string(),
                    value: ValueX::ValueString(format!("Invalid language tag: {}. {}.", code, v.to_string())),
                }],
                concept: None,
            },
        }
    }
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
        println!("{:#?}", result);
        assert_eq!(result.concept, expected.concept);
        assert_eq!(result.details.len(), expected.details.len());
        for (actual_detail, expected_detail) in result.details.iter().zip(expected.details.iter()) {
            assert_eq!(actual_detail.severity, expected_detail.severity);
            // assert_eq!(actual_detail.key, expected_detail.key);
            // assert_eq!(actual_detail.value, expected_detail.value);
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
        let parser = Parser::new(&db);

        let code = "en".to_string();
        let result = parser.parse(code.clone(), None);
        let expected = create_expected_result(&code, vec![("language", "en")], vec![]);
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_language_with_script() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("zh", "language", Some("Chinese")));
        db.insert(create_concept("Hant", "script", Some("Traditional")));
        let parser = Parser::new(&db);

        let code = "zh-Hant".to_string();
        let result = parser.parse(code.clone(), None);
        let expected =
            create_expected_result(&code, vec![("language", "zh"), ("script", "Hant")], vec![]);
        assert_parse_result(result, expected);
    }

    #[test]
    fn test_parse_language_with_region() {
        let mut db = mock_terminology_db::MockTerminologyDb::new();
        db.insert(create_concept("en", "language", Some("English")));
        db.insert(create_concept("US", "region", Some("United States")));
        let parser = Parser::new(&db);

        let code = "en-US".to_string();
        let result = parser.parse(code.clone(), None);
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
        let parser = Parser::new(&db);

        let code = "sl-IT-nedis-rozaj".to_string();
        let result = parser.parse(code.clone(), None);
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
        let parser = Parser::new(&db);

        let code = "en-US-u-co-phonebk".to_string();
        let result = parser.parse(code.clone(), None);
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
        let parser = Parser::new(&db);

        let code = "en-x-shhabc".to_string();
        let result = parser.parse(code.clone(), None);
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
        let mut parser = Parser::new(&db);

        let code = "invalid".to_string();
        let result = parser.parse(code.clone(), None);
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
        let parser = Parser::new(&db);

        let code = "en-abc".to_string();
        let result = parser.parse(code.clone(), None);
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
        let parser = Parser::new(&db);

        let code = "en-US-u-co-phonebk-x-priv".to_string();
        let result = parser.parse(code.clone(), None);
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
        let parser = Parser::new(&db);

        let code = "en-US-u-co-phonebk-x-private".to_string();
        let result = parser.parse(code.clone(), None);
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
        let parser = Parser::new(&db);

        let code = "en-US-u".to_string();
        let result = parser.parse(code.clone(), None);
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
