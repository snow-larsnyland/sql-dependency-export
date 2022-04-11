﻿function handleFileSelect(evt) {
    let files = evt.target.files; // FileList object

    // use the 1st file from the list
    let f = files[0];

    let reader = new FileReader();

    // Closure to capture the file information.
    reader.onload = (function (theFile) {
        return function (e) {
            renderTree(e.target.result);
        };
    })(f);

    // Read in the image file as a data URL.
    reader.readAsText(f);
}

function renderTree(jsonStr) {
    debugger;
    var tree = JSON.parse(jsonStr);
}

document.getElementById('dependencyFile').addEventListener('change', handleFileSelect, false);