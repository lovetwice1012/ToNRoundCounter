const fs = require('fs');
const path = require('path');

// Replace datetime('now') with NOW() in all TypeScript files
const replaceInDirectory = (dirPath) => {
    const files = fs.readdirSync(dirPath, { withFileTypes: true });
    
    for (const file of files) {
        const fullPath = path.join(dirPath, file.name);
        
        if (file.isDirectory()) {
            replaceInDirectory(fullPath);
        } else if (file.name.endsWith('.ts')) {
            let content = fs.readFileSync(fullPath, 'utf8');
            const original = content;
            
            // Replace datetime('now') with NOW()
            content = content.replace(/datetime\('now'\)/g, "NOW()");
            content = content.replace(/datetime\(\\'now\\'\)/g, "NOW()");
            
            if (content !== original) {
                fs.writeFileSync(fullPath, content, 'utf8');
                console.log(`Updated: ${fullPath}`);
            }
        }
    }
};

const srcPath = path.join(__dirname, 'src');
console.log(`Scanning ${srcPath}...`);
replaceInDirectory(srcPath);
console.log('Done!');
