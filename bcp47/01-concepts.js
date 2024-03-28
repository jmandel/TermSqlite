// Input from wget https://www.iana.org/assignments/language-subtag-registry/language-subtag-registry
const readline = require('readline');
const fs = require('fs');
const path = require('path');
const bcp47 = JSON.parse(fs.readFileSync(path.join(__dirname, 'templates', 'CodeSystem-bcp47.json'), 'utf8'));

function parseConcepts() {
  return new Promise((resolve) => {
    const concepts = [];
    let currentConcept = null;
    const properties = new Set();

    const rl = readline.createInterface({
      input: process.stdin,
      terminal: false,
    });

    let l = 0;
    rl.on('line', (line) => {
      if (l == 0){
        l++
        return;
      }
      if (line.startsWith('%%')) {
        if (currentConcept) {
          concepts.push(currentConcept);
        }
        currentConcept = {
          code: '',
          display: '',
          property: [],
        };
      } else if (line.startsWith('Type:')) {
        const [, type] = line.split(':').map((part) => part.trim());
        currentConcept.code = `${type.toLowerCase()}-`;
      } else if (line.startsWith('Subtag:')) {
        const [, subtag] = line.split(':').map((part) => part.trim());
        currentConcept.code  += subtag;
      } else if (line.startsWith('Description:')) {
        const [, description] = line.split(':').map((part) => part.trim());
        currentConcept.display = description;
      } else if (line.includes(':')) {
        const [code, value] = line.split(':').map((part) => part.trim());
        const propertyType = ["Added", "Deprecated"].includes(code)? "valueDateTime" : "valueString";
        currentConcept.property.push({
          code,
          [propertyType]: value,
        });
        const camelCaseWithoutValue = propertyType.slice(5).charAt(0).toLowerCase() + propertyType.slice(6);
        properties.add(JSON.stringify({code, type: camelCaseWithoutValue}));
      }
    });

    rl.on('close', () => {
      if (currentConcept) {
        concepts.push(currentConcept);
      }
      resolve({
        concept: concepts.filter(c => !["redundant-", "grandfathered-"].includes(c.code)),
        property: Array.from(properties).map(p => JSON.parse(p)),
      });
    });
  });
}

async function main() {
  const { concept, property } = await parseConcepts();
  console.log(JSON.stringify({...bcp47, property }));
  for (const c of concept) {
    console.log(JSON.stringify(c));
  }
}

main();