(function () {
    const pluginId = '7184fe02-8e91-4fd2-9140-6d58d5e91f0a';
    let statusTimer;
    let activeExportId = '';
    let lastNotifiedExportId = '';

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
        const writableOptions = (options || []).filter(option => option.isWritable !== false);
        const defaultOption = writableOptions.find(option => option.isDefault) || writableOptions[0];

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

        return url + (url.indexOf('?') === -1 ? '?' : '&') + 'api_key=' + encodeURIComponent(token);
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

    function renderProgress(status) {
        const container = page().querySelector('#exportProgress');
        const bar = container.querySelector('.exportProgressBar');
        const fill = page().querySelector('#exportProgressFill');
        const stage = status.stage || 'Idle';
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

    function maybeNotifyExportFinished(status) {
        const exportId = status.exportId || activeExportId;
        if (!exportId || status.isRunning || lastNotifiedExportId === exportId) {
            return;
        }

        if (status.errorMessage || status.stage === 'Failed') {
            showToast(`Library inventory export failed: ${status.errorMessage || 'Unknown error'}`);
            lastNotifiedExportId = exportId;
            return;
        }

        if (status.stage === 'Completed' || clampPercent(status.progressPercent) === 100) {
            showToast('Library inventory export completed.');
            lastNotifiedExportId = exportId;
        }
    }

    function renderStatus(status) {
        renderProgress(status);
        maybeNotifyExportFinished(status);

        const percent = clampPercent(status.progressPercent);
        const statusText = `${status.stage || 'Idle'} ${percent}%`;
        page().querySelector('#exportStatus').innerText = status.errorMessage ? `${statusText}: ${status.errorMessage}` : statusText;
        setExportButtonRunning(status.isRunning === true);

        if (status.isRunning) {
            activeExportId = status.exportId || activeExportId;
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

    function refreshStatus() {
        return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('InventoryExporter/Status') }).then(renderStatus);
    }

    function refreshExports() {
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('InventoryExporter/Exports') }).then(exports => {
            page().querySelector('#exportHistory').innerHTML = exports.length === 0
                ? '<p>No exports yet.</p>'
                : exports.map(item => `<p><a href="${getDownloadUrl('InventoryExporter/Exports/' + item.id + '/Download')}">${item.fileName}</a> ${item.itemCount} items <button is="emby-button" type="button" data-delete-export="${item.id}">Delete</button></p>`).join('');
        });
    }

    function refreshHistory() {
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('InventoryExporter/OutputDirectories') }).then(renderOutputDirectories);
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('InventoryExporter/Libraries') }).then(libraries => {
            renderLibraries(libraries);
            updateLibraryPickerState();
        });
        refreshStatus();
        refreshExports();
    }

    document.addEventListener('pageshow', event => {
        if (!event.target.matches('#LibraryInventoryExporterConfigPage')) {
            return;
        }

        loadConfig().then(refreshHistory);
    });

    document.addEventListener('submit', event => {
        if (!event.target.matches('#LibraryInventoryExporterConfigForm')) {
            return;
        }

        event.preventDefault();
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
        });
    });

    document.addEventListener('click', event => {
        if (event.target.closest('#btnRunExport')) {
            ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('InventoryExporter/Export'),
                data: JSON.stringify({ formats: ['csv', 'json'], libraryIds: selectedLibraryIds() }),
                contentType: 'application/json'
            }).then(response => {
                activeExportId = response && response.exportId ? response.exportId : '';
                lastNotifiedExportId = '';
                page().querySelector('#exportStatus').innerText = 'Starting export 0%';
                renderProgress({ isRunning: true, exportId: activeExportId, stage: 'Starting export', progressPercent: 0 });
                setExportButtonRunning(true);
                showToast('Library inventory export started.');
                scheduleStatusRefresh();
            });
        }

        if (event.target.closest('#btnDownloadLatest')) {
            window.location.href = getDownloadUrl('InventoryExporter/Exports/Latest');
        }

        const outputDirectoryButton = event.target.closest('[data-output-directory]');
        if (outputDirectoryButton) {
            page().querySelector('#txtOutputDirectory').value = outputDirectoryButton.getAttribute('data-output-directory');
        }

        const deleteButton = event.target.closest('[data-delete-export]');
        if (deleteButton) {
            ApiClient.ajax({ type: 'DELETE', url: ApiClient.getUrl('InventoryExporter/Exports/' + deleteButton.getAttribute('data-delete-export')) }).then(refreshHistory);
        }
    });

    document.addEventListener('change', event => {
        if (event.target.matches('#chkAllLibraries')) {
            updateLibraryPickerState();
        }
    });
})();
