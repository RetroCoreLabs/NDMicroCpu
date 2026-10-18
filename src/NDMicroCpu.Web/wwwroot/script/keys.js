function addKeyListener(dotnetObj) {
    document.addEventListener("keydown", function (e) {
        dotnetObj.invokeMethodAsync('OnKeyDown', e.key);
    });
    document.addEventListener("keyup", function (e) {
        dotnetObj.invokeMethodAsync('OnKeyUp', e.key);
    });
}

function removeKeyListener(dotnetObj) {
    document.removeEventListener("keydown", function (e) {
        dotnetObj.invokeMethodAsync('OnKeyDown', e.key);
    });
    document.removeEventListener("keyup", function (e) {
        dotnetObj.invokeMethodAsync('OnKeyUp', e.key);
    });
}
