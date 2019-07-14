pcanvas = [];
pscale = 8;

const canvasWidth = 50;
const canvasHeight = 50;

acceptInput = false;

socket = null;

function setup() {
    socket = new WebSocket('ws://localhost:8080');

    // Connection opened
    socket.addEventListener('open', function (event) {
        requestCanvas();
    });

    // Listen for messages
    socket.addEventListener('message', function (event) {
        console.debug('>> ', event.data);

        var args = event.data.split(';');

        switch (args[0]) {
            default: console.warn("Unknown command", args[0]); break;
            case "c":
                var y = parseInt(args[1]);

                for (let x = 0; x < canvasWidth; x++) {
                    if (pcanvas[x] == undefined) {
                        pcanvas[x] = [];
                    }
                    pcanvas[x][y] = args[2 + x];
                }

                acceptInput = true;
                break;

            case "pc":
                var x = parseInt(args[1]);
                var y = parseInt(args[2]);

                if (pcanvas[x] == undefined) {
                    pcanvas[x] = [];
                }

                pcanvas[x][y] = args[3];
                break;

        }
    });

    socket.addEventListener('error', function (event) {
        console.error(event);
    });

    socket.addEventListener('close', function (event) {
        console.warn("closed", event);
    });

    createCanvas(canvasWidth * pscale, canvasHeight * pscale);
    noStroke();
    frameRate(60);
}

function requestCanvas() {
    socket.send("rc;");
}

function draw() {
    if (pcanvas.length != undefined) {
        for (let x = 0; x < canvasWidth; x++) {
            for (let y = 0; y < canvasHeight; y++) {

                if (pcanvas[x] != undefined && pcanvas[x][y] != undefined) {
                    fill(pcanvas[x][y]);

                    if (acceptInput && getMouseCanvasX() == x && getMouseCanvasY() == y) {
                        stroke("red");
                    } else {
                        noStroke();
                    }

                    rect(x * pscale, y * pscale, pscale, pscale);
                }


            }
        }
    }
}

function getMouseCanvasX() {
    return Math.round(mouseX / pscale);
}

function getMouseCanvasY() {
    return Math.round(mouseY / pscale);
}

function mouseClicked() {
    if (!acceptInput) return;


    var x = getMouseCanvasX().toString();
    var y = getMouseCanvasY().toString();

    if (x > canvasWidth || y > canvasHeight) {
        return;
    }

    socket.send("sp;" + x + ";" + y + ";#FF0000");
}

function mouseWheel(event) {
    pscale += event.delta / -100;
    console.debug(event.delta);
    resizeCanvas(canvasWidth * pscale, canvasHeight * pscale);

    return false;
}