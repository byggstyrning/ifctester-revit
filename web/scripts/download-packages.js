import { execSync } from 'child_process';
import { writeFileSync, mkdirSync, existsSync } from 'fs';
import { join } from 'path';

/**
 * Download required Python packages for Pyodide during build
 */
async function downloadPyodidePackages() {
    const publicDir = join(process.cwd(), 'public');
    const pyodideDir = join(publicDir, 'pyodide');
    const workerBinDir = join(publicDir, 'worker', 'bin');
    
    // Ensure directories exist
    if (!existsSync(pyodideDir)) {
        mkdirSync(pyodideDir, { recursive: true });
    }
    if (!existsSync(workerBinDir)) {
        mkdirSync(workerBinDir, { recursive: true });
    }
    
    console.log('Downloading Pyodide packages...');
    
    // Packages that need to be downloaded
    const packages = [
        {
            name: 'ifctester',
            url: 'https://files.pythonhosted.org/packages/source/i/ifctester/ifctester-0.0.6.tar.gz',
            // Try wheel first, fallback to source
            wheelUrl: 'https://files.pythonhosted.org/packages/py3/i/ifctester/ifctester-0.0.6-py3-none-any.whl'
        }
    ];
    
    // Download ifctester wheel
    try {
        console.log('Downloading ifctester...');
        const ifctesterUrl = 'https://files.pythonhosted.org/packages/py3/i/ifctester/ifctester-0.0.6-py3-none-any.whl';
        const outputPath = join(workerBinDir, 'ifctester-0.0.6-py3-none-any.whl');
        
        // Use curl or fetch to download
        execSync(`curl -L "${ifctesterUrl}" -o "${outputPath}"`, { stdio: 'inherit' });
        console.log('✓ Downloaded ifctester');
    } catch (error) {
        console.warn('Failed to download ifctester, will try to install from PyPI at runtime:', error.message);
    }
}

// Run if called directly
if (import.meta.url === `file://${process.argv[1]}`) {
    downloadPyodidePackages().catch(console.error);
}

export default downloadPyodidePackages;


