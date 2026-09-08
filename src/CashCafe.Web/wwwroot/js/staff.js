// Keeps the slider's label and bar in step as it is dragged, and saves when it is let go.
// The form still works with this file blocked or JavaScript off — the Set button submits it —
// which matters on a school phone with a locked-down browser.

(function () {
    var form = document.getElementById('busyness-form');
    if (!form) return;

    var slider = form.querySelector('input[type=range]');
    var label = document.getElementById('busyness-label');
    var bar = form.querySelector('.busy');
    var names = ['Closed', 'Quiet', 'Steady', 'Busy', 'Packed'];

    function paint() {
        var level = Number(slider.value);
        label.textContent = names[level];
        bar.className = 'busy level-' + level;

        var steps = bar.querySelectorAll('span');
        for (var i = 0; i < steps.length; i++) {
            steps[i].className = (i + 1) <= level ? 'on' : '';
        }
    }

    slider.addEventListener('input', paint);

    // Saving on release rather than on every step: dragging from Closed to Packed would
    // otherwise write four rows to the audit log on the way past.
    slider.addEventListener('change', function () { form.submit(); });

    paint();
})();
