use extism_pdk::{host_fn, info, log, plugin_fn, FnResult, LogLevel};
use extism_pdk::{FromBytes, Json, ToBytes};
use nom::error::ParseError;
use serde_derive::{Deserialize, Serialize};

pub struct Guest<T>
where
    T: TerminologyDb,
{
    pub(crate) db: T,
}

impl Default for Guest<HostReal> {
    fn default() -> Self {
        Guest::new(HostReal::new())
    }
}

impl<T> Guest<T>
where
    T: TerminologyDb,
{
    pub(crate) fn new(host: T) -> Self {
        Guest { db: host }
    }
}

impl ParseError<&str> for ParseDetail {
    fn from_error_kind(input: &str, kind: nom::error::ErrorKind) -> Self {
        ParseDetail {
            severity: Severity::Error,
            key: "error".to_string(),
            value: ValueX::ValueString(format!(
                "Error parsing input: {:?} with kind: {:?}",
                input, kind
            )),
        }
    }

    fn append(input: &str, kind: nom::error::ErrorKind, _other: Self) -> Self {
        ParseDetail {
            severity: Severity::Error,
            key: "error".to_string(),
            value: ValueX::ValueString(format!(
                "Error parsing input: {:?} with kind: {:?}",
                input, kind
            )),
        }
    }
}

pub trait TerminologyEngine<T: TerminologyDb> {
    fn metadata(&self) -> String;
    fn parse(&self, req: ParseRequest) -> ParseResponse;
    fn subsumes(&self, req: SubsumesRequest) -> SubsumesResponse;
}

pub trait TerminologyDb {
    fn db_lookup(&self, req: LookupRequest) -> LookupResponse;
    fn db_subsumes(&self, req: SubsumesRequest) -> SubsumesResponse;
}

pub trait WithDb {
    fn db(&self) -> &dyn TerminologyDb;
}

#[derive(Copy, Clone)]
pub struct HostReal {}

impl HostReal {
    pub fn new() -> Self {
        HostReal {}
    }
}

#[host_fn]
extern "ExtismHost" {
    fn db_lookup(input: LookupRequest) -> LookupResponse;
}

#[host_fn]
extern "ExtismHost" {
    fn db_subsumes(input: LookupRequest) -> LookupResponse;
}
impl TerminologyDb for HostReal {
    fn db_lookup(&self, req: LookupRequest) -> LookupResponse {
        log!(LogLevel::Info, "Calling host");
        unsafe {
            let res = db_lookup(req);
            info!("Got back : {:?}", res);
            res.unwrap_or(LookupResponse { concept: None })
        }
    }

    fn db_subsumes(&self, _req: SubsumesRequest) -> SubsumesResponse {
        todo!()
    }
}

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub enum ValueX {
    #[serde(rename = "valueString")]
    ValueString(String),
    #[serde(rename = "valueDateTime")]
    ValueDateTime(String),
    #[serde(rename = "valueCode")]
    ValueCode(String),
    #[serde(rename = "valueCoding")]
    ValueCoding(Coding),
    #[serde(rename = "valueDecimal")]
    ValueDecimal(String),
}

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct Coding {
    pub system: Option<String>,
    pub code: Option<String>,
    pub display: Option<String>,
}

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct Property {
    pub code: String,
    #[serde(flatten)]
    pub value: ValueX,
}
#[derive(
    Default, Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct Concept {
    pub code: String,
    pub display: Option<String>,
    pub properties: Vec<Property>,
}


#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
#[serde(rename_all = "lowercase")]
pub enum Severity {
    Error,
    Warning,
    Information,
    Success,
}

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct ParseDetail {
    pub severity: Severity,
    pub key: String,
    #[serde(flatten)]
    pub value: ValueX,
}

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct ParseResponse {
    pub details: Vec<ParseDetail>,
    pub concept: Option<Concept>,
}

// terminology_db.rs

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct LookupRequest {
    pub code: String,
    pub properties: Option<Vec<String>>,
}

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct LookupResponse {
    pub concept: Option<Concept>,
}

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct SubsumesRequest {
    pub ancestor: String,
    pub descendant: String,
}

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct SubsumesResponse {
    pub subsumes: bool,
}

// terminology_engine.rs

#[derive(
    Deserialize, Serialize, PartialEq, Eq, PartialOrd, Ord, Clone, Debug, ToBytes, FromBytes,
)]
#[encoding(Json)]
pub struct ParseRequest {
    pub code: String,
    pub properties: Option<Vec<String>>,
}

pub use lazy_static::lazy_static;

#[macro_export]
macro_rules! define_terminology_engine {
    ($engine_type:ty) => {
        lazy_static! {
            static ref TERMINOLOGY_ENGINE: $engine_type = <$engine_type>::default();
        }

        #[plugin_fn]
        pub fn metadata() -> FnResult<String> {
            Ok(TERMINOLOGY_ENGINE.metadata())
        }

        #[plugin_fn]
        pub fn parse(req: ParseRequest) -> FnResult<ParseResponse> {
            Ok(TERMINOLOGY_ENGINE.parse(req))
        }

        #[plugin_fn]
        pub fn subsumes(req: SubsumesRequest) -> FnResult<SubsumesResponse> {
            Ok(TERMINOLOGY_ENGINE.subsumes(req))
        }
    };
}

define_terminology_engine!(Guest<HostReal>);

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::to_string;

    #[test]
    fn test_parse_request_response() {
        let request = ParseRequest {
            code: "test_code".to_string(),
            properties: Some(vec!["property1".to_string(), "property2".to_string()]),
            // Add more properties here as needed
        };

        let response = ParseResponse {
            details: vec![
                ParseDetail {
                    severity: Severity::Error,
                    key: "key1".to_string(),
                    value: ValueX::ValueCode("OK".to_string()),
                },
                // Add more Detail structs here as needed
            ],
            concept: Some(Concept {
                code: "concept_code".to_string(),
                properties: vec![
                    Property {
                        code: "property1".to_string(),
                        value: ValueX::ValueString("value1".to_string()),
                    },
                    Property {
                        code: "property2".to_string(),
                        value: ValueX::ValueCoding(Coding {
                            system: Some("system1".to_string()),
                            code: Some("code1".to_string()),
                            display: Some("display1".to_string()),
                            // Add more properties here as needed
                        }),
                    },
                    // Add more Property structs here as needed
                ],
                ..Concept::default()
            }),
        };

        let request_json = to_string(&request).unwrap();
        let response_json = to_string(&response).unwrap();

        println!("Request: {}", request_json);
        println!("Response: {}", response_json);
    }
}
