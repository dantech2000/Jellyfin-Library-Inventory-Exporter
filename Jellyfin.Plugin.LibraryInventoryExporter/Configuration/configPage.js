(function () {
    const pluginId = '7184fe02-8e91-4fd2-9140-6d58d5e91f0a';
    let statusTimer;
    let activeExportId = '';
    let lastNotifiedExportId = '';

    // Hints for failed requests that come without a message from the server.
    const httpErrorHints = {
        401: 'Your Jellyfin session has ended. Sign in again.',
        403: 'Only Jellyfin administrators can use the Library Inventory Exporter.',
        404: 'The server could not find what the page asked for. Refresh the page.',
        500: 'The Jellyfin server hit an error. Check the Jellyfin log for details.'
    };

    function page() {
        return document.querySelector('#LibraryInventoryExporterConfigPage');
    }

    function loadConfig() {
        return ApiClient.getPluginConfiguration(pluginId).then(config => {
            page().querySelector('#txtOutputDirectory').value = config.OutputDirectory || '';
            page().querySelector('#chkCsv').checked = config.ExportCsvByDefault !== false;
            page().querySelector('#chkJson').checked = config.ExportJsonByDefault !== false;
            page().querySelector('#chkStreams').checked = config.IncludeMediaStreams !== false;
            page().querySelector('#chkProviders').checked = config.IncludeProviderIds !== false;
            page().querySelector('#chkUserData').checked = config.IncludeUserData === true;
            page().querySelector('#txtRetentionCount').value = config.RetentionCount || 10;
            page().querySelector('#txtRetentionDays').value = config.RetentionDays || 90;
        });
    }

    function escapeHtml(value) {
        return String(value || '').replace(/[&<>"']/g, character => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        })[character]);
    }

    // Jellyfin serializes plugin API responses in PascalCase. The page reads them in camelCase.
    function camelCaseKeys(value) {
        if (Array.isArray(value)) {
            return value.map(camelCaseKeys);
        }

        if (value && typeof value === 'object') {
            return Object.keys(value).reduce((result, key) => {
                result[key.charAt(0).toLowerCase() + key.slice(1)] = camelCaseKeys(value[key]);
                return result;
            }, {});
        }

        return value;
    }

    function getJson(route) {
        return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl(route), dataType: 'json' }).then(camelCaseKeys);
    }

    function showError(message) {
        const panel = page().querySelector('#inventoryExporterError');
        panel.innerText = message;
        panel.hidden = false;
        panel.scrollIntoView({ block: 'nearest' });
    }

    function clearError() {
        const panel = page().querySelector('#inventoryExporterError');
        panel.innerText = '';
        panel.hidden = true;
    }

    // ApiClient rejects with the fetch Response for HTTP errors and with an Error when the server is unreachable.
    function describeError(error) {
        if (!error || typeof error.status !== 'number') {
            return Promise.resolve((error && error.message) || 'The Jellyfin server did not answer. Check that it is running, then refresh the page.');
        }

        const fallback = httpErrorHints[error.status] || `The server answered with HTTP ${error.status}.`;
        if (typeof error.text !== 'function') {
            return Promise.resolve(fallback);
        }

        return error.text().then(body => {
            try {
                const problem = JSON.parse(body);
                return problem.detail || problem.Detail || problem.title || problem.Title || fallback;
            } catch (parseError) {
                return fallback;
            }
        }, () => fallback);
    }

    function reportError(action, error) {
        return describeError(error).then(reason => showError(`${action}: ${reason}`));
    }

    function renderLibraries(libraries) {
        const list = page().querySelector('#libraryList');
        if (!libraries || libraries.length === 0) {
            list.innerHTML = '<div class="fieldDescription">No libraries found. Exports will include all available libraries.</div>';
            return;
        }

        list.innerHTML = libraries.map(library => `
            <label class="checkboxContainer libraryOption">
                <input is="emby-checkbox" type="checkbox" class="chkLibrary" data-library-id="${escapeHtml(library.id)}" disabled />
                <span>${escapeHtml(library.name)}</span>
            </label>
        `).join('');
    }

    function renderOutputDirectories(options) {
        const input = page().querySelector('#txtOutputDirectory');
        const help = page().querySelector('#outputDirectoryHelp');
        const container = page().querySelector('#outputDirectoryOptions');
        const warning = page().querySelector('#outputDirectoryWarning');
        const currentOption = (options || []).find(option => option.isCurrent);
        const writableOptions = (options || []).filter(option => option.isWritable !== false);
        const defaultOption = writableOptions.find(option => option.isDefault) || writableOptions[0];

        if (currentOption && currentOption.isWritable === false) {
            warning.innerText = `Jellyfin cannot write to ${currentOption.path}. Exports will fail until you choose a directory that the server can write to and save the settings.`;
            warning.hidden = false;
        } else {
            warning.innerText = '';
            warning.hidden = true;
        }

        if (!input.value && defaultOption) {
            input.value = defaultOption.path;
        }

        if (!defaultOption) {
            help.innerText = 'Enter a server path that the Jellyfin process can write to.';
            container.innerHTML = '';
            return;
        }

        help.innerText = 'Exports are written by the Jellyfin server. In Docker, use a container path backed by a writable volume.';
        container.innerHTML = writableOptions.map(option => `
            <button is="emby-button" type="button" class="raised outputDirectoryOption" data-output-directory="${escapeHtml(option.path)}">
                <span>${escapeHtml(option.label)}: ${escapeHtml(option.path)}</span>
            </button>
        `).join('');
    }

    function selectedLibraryIds() {
        if (page().querySelector('#chkAllLibraries').checked) {
            return [];
        }

        return Array.from(page().querySelectorAll('.chkLibrary:checked')).map(input => input.getAttribute('data-library-id'));
    }

    function getAccessToken() {
        if (typeof ApiClient.accessToken === 'function') {
            return ApiClient.accessToken();
        }

        if (typeof ApiClient.accessToken === 'string') {
            return ApiClient.accessToken;
        }

        if (ApiClient._serverInfo && ApiClient._serverInfo.AccessToken) {
            return ApiClient._serverInfo.AccessToken;
        }

        return '';
    }

    function getDownloadUrl(route) {
        const url = ApiClient.getUrl(route);
        const token = getAccessToken();
        if (!token) {
            return url;
        }

        return url + (url.indexOf('?') === -1 ? '?' : '&') + 'ApiKey=' + encodeURIComponent(token);
    }

    function updateLibraryPickerState() {
        const allLibraries = page().querySelector('#chkAllLibraries').checked;
        page().querySelectorAll('.chkLibrary').forEach(input => {
            input.disabled = allLibraries;
            if (allLibraries) {
                input.checked = false;
            }
        });
    }

    function setExportButtonRunning(isRunning) {
        const button = page().querySelector('#btnRunExport');
        button.disabled = isRunning;
        button.querySelector('span').innerText = isRunning ? 'Export Running' : 'Run Export Now';
    }

    function showToast(message) {
        if (typeof Dashboard !== 'undefined' && typeof Dashboard.toast === 'function') {
            Dashboard.toast(message);
            return;
        }

        if (typeof Dashboard !== 'undefined' && typeof Dashboard.alert === 'function') {
            Dashboard.alert(message);
        }
    }

    function clampPercent(value) {
        const percent = Number(value);
        if (!Number.isFinite(percent)) {
            return 0;
        }

        return Math.max(0, Math.min(100, Math.round(percent)));
    }

    function statusValue(status, camelName, pascalName, fallback) {
        if (Object.prototype.hasOwnProperty.call(status, camelName)) {
            return status[camelName];
        }

        if (Object.prototype.hasOwnProperty.call(status, pascalName)) {
            return status[pascalName];
        }

        return fallback;
    }

    function normalizeStatus(status) {
        return {
            isRunning: statusValue(status, 'isRunning', 'IsRunning', false) === true,
            exportId: statusValue(status, 'exportId', 'ExportId', ''),
            stage: statusValue(status, 'stage', 'Stage', 'Idle') || 'Idle',
            progressPercent: statusValue(status, 'progressPercent', 'ProgressPercent', 0),
            processedItems: statusValue(status, 'processedItems', 'ProcessedItems', 0),
            totalItems: statusValue(status, 'totalItems', 'TotalItems', 0),
            errorMessage: statusValue(status, 'errorMessage', 'ErrorMessage', '')
        };
    }

    function renderProgress(rawStatus) {
        const status = normalizeStatus(rawStatus);
        const container = page().querySelector('#exportProgress');
        const bar = container.querySelector('.exportProgressBar');
        const fill = page().querySelector('#exportProgressFill');
        const stage = status.stage;
        const percent = clampPercent(status.progressPercent);
        const processed = Number(status.processedItems || 0);
        const total = Number(status.totalItems || 0);

        container.hidden = !status.isRunning && percent === 0 && !status.errorMessage && stage === 'Idle';
        page().querySelector('#exportProgressStage').innerText = stage;
        page().querySelector('#exportProgressPercent').innerText = `${percent}%`;
        fill.style.width = `${percent}%`;
        bar.setAttribute('aria-valuenow', String(percent));

        if (status.errorMessage) {
            page().querySelector('#exportProgressDetails').innerText = status.errorMessage;
        } else if (total > 0) {
            page().querySelector('#exportProgressDetails').innerText = `${processed} of ${total} items processed`;
        } else {
            page().querySelector('#exportProgressDetails').innerText = status.isRunning ? 'Export in progress' : '';
        }
    }

    // Toasts only for exports started or seen running on this page. A failed export also stays in the error panel,
    // so an administrator who opens the page later still sees why the last export failed.
    function maybeNotifyExportFinished(rawStatus) {
        const status = normalizeStatus(rawStatus);
        const exportId = status.exportId || activeExportId;
        if (!exportId || status.isRunning || lastNotifiedExportId === exportId) {
            return;
        }

        const watched = exportId === activeExportId;
        lastNotifiedExportId = exportId;

        if (status.errorMessage || status.stage === 'Failed') {
            const message = `Library inventory export failed: ${status.errorMessage || 'Unknown error'}`;
            if (watched) {
                showToast(message);
            }

            showError(message);
            return;
        }

        if (watched && (status.stage === 'Completed' || clampPercent(status.progressPercent) === 100)) {
            showToast('Library inventory export completed.');
        }
    }

    function renderStatus(rawStatus) {
        const status = normalizeStatus(rawStatus);
        renderProgress(status);

        if (status.isRunning) {
            activeExportId = status.exportId || activeExportId;
        }

        maybeNotifyExportFinished(status);

        const percent = clampPercent(status.progressPercent);
        const statusText = `${status.stage} ${percent}%`;
        page().querySelector('#exportStatus').innerText = status.errorMessage ? `${statusText}: ${status.errorMessage}` : statusText;
        setExportButtonRunning(status.isRunning === true);

        if (status.isRunning) {
            scheduleStatusRefresh();
            return;
        }

        clearStatusRefresh();
        refreshExports();
    }

    function clearStatusRefresh() {
        if (statusTimer) {
            clearTimeout(statusTimer);
            statusTimer = null;
        }
    }

    function scheduleStatusRefresh() {
        clearStatusRefresh();
        statusTimer = setTimeout(refreshStatus, 2000);
    }

    function scheduleInitialStatusRefresh() {
        clearStatusRefresh();
        statusTimer = setTimeout(refreshStatus, 250);
    }

    function refreshStatus() {
        return getJson('InventoryExporter/Status').then(renderStatus, error => {
            reportError('Could not load the export status', error);
            if (activeExportId) {
                // Keep following a running export through short outages.
                scheduleStatusRefresh();
            }
        });
    }

    function refreshExports() {
        return getJson('InventoryExporter/Exports').then(exports => {
            page().querySelector('#exportHistory').innerHTML = exports.length === 0
                ? '<p>No exports yet.</p>'
                : exports.map(item => `<p><a href="${getDownloadUrl('InventoryExporter/Exports/' + item.id + '/Download')}">${escapeHtml(item.fileName)}</a> ${escapeHtml(item.itemCount)} items <button is="emby-button" type="button" data-delete-export="${escapeHtml(item.id)}">Delete</button></p>`).join('');
        }, error => reportError('Could not load the recent exports', error));
    }

    function refreshOutputDirectories() {
        return getJson('InventoryExporter/OutputDirectories').then(renderOutputDirectories, error => reportError('Could not load the output directory suggestions', error));
    }

    function refreshHistory() {
        refreshOutputDirectories();
        getJson('InventoryExporter/Libraries').then(libraries => {
            renderLibraries(libraries);
            updateLibraryPickerState();
        }, error => reportError('Could not load the libraries', error));
        refreshStatus();
        refreshExports();
    }

    document.addEventListener('pageshow', event => {
        if (!event.target.matches('#LibraryInventoryExporterConfigPage')) {
            return;
        }

        clearError();
        loadConfig().then(refreshHistory, error => {
            reportError('Could not load the settings', error);
            refreshHistory();
        });
    });

    document.addEventListener('submit', event => {
        if (!event.target.matches('#LibraryInventoryExporterConfigForm')) {
            return;
        }

        event.preventDefault();
        clearError();
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then(config => {
            config.OutputDirectory = page().querySelector('#txtOutputDirectory').value;
            config.ExportCsvByDefault = page().querySelector('#chkCsv').checked;
            config.ExportJsonByDefault = page().querySelector('#chkJson').checked;
            config.IncludeMediaStreams = page().querySelector('#chkStreams').checked;
            config.IncludeProviderIds = page().querySelector('#chkProviders').checked;
            config.IncludeUserData = page().querySelector('#chkUserData').checked;
            config.RetentionCount = parseInt(page().querySelector('#txtRetentionCount').value || '10', 10);
            config.RetentionDays = parseInt(page().querySelector('#txtRetentionDays').value || '90', 10);
            return ApiClient.updatePluginConfiguration(pluginId, config);
        }).then(() => {
            Dashboard.hideLoadingMsg();
            Dashboard.processPluginConfigurationUpdateResult();
            refreshOutputDirectories();
        }, error => {
            Dashboard.hideLoadingMsg();
            reportError('Could not save the settings', error);
        });
    });

    document.addEventListener('click', event => {
        if (event.target.closest('#btnRunExport')) {
            clearError();
            // No formats in the request: the server applies the saved CSV and JSON defaults.
            ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('InventoryExporter/Export'),
                data: JSON.stringify({ libraryIds: selectedLibraryIds() }),
                contentType: 'application/json',
                dataType: 'json'
            }).then(response => {
                const startStatus = normalizeStatus(camelCaseKeys(response || {}));
                activeExportId = startStatus.exportId;
                lastNotifiedExportId = '';
                page().querySelector('#exportStatus').innerText = 'Starting export 0%';
                renderProgress({ isRunning: true, exportId: activeExportId, stage: 'Starting export', progressPercent: 0 });
                setExportButtonRunning(true);
                showToast('Library inventory export started.');
                scheduleInitialStatusRefresh();
            }, error => reportError('Could not start the export', error));
        }

        if (event.target.closest('#btnDownloadLatest')) {
            clearError();
            getJson('InventoryExporter/Exports').then(exports => {
                if (exports.length === 0) {
                    showError('There is no export to download yet. Run an export first.');
                    return;
                }

                window.location.href = getDownloadUrl('InventoryExporter/Exports/Latest');
            }, error => reportError('Could not download the latest export', error));
        }

        const outputDirectoryButton = event.target.closest('[data-output-directory]');
        if (outputDirectoryButton) {
            page().querySelector('#txtOutputDirectory').value = outputDirectoryButton.getAttribute('data-output-directory');
        }

        const deleteButton = event.target.closest('[data-delete-export]');
        if (deleteButton) {
            clearError();
            ApiClient.ajax({ type: 'DELETE', url: ApiClient.getUrl('InventoryExporter/Exports/' + deleteButton.getAttribute('data-delete-export')) })
                .then(refreshHistory, error => reportError('Could not delete the export', error));
        }
    });

    document.addEventListener('change', event => {
        if (event.target.matches('#chkAllLibraries')) {
            updateLibraryPickerState();
        }
    });
})();
