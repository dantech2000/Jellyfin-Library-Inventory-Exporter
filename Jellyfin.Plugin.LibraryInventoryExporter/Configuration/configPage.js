(function () {
    const pluginId = '7184fe02-8e91-4fd2-9140-6d58d5e91f0a';
    let statusTimer;

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

    function updateLibraryPickerState() {
        const allLibraries = page().querySelector('#chkAllLibraries').checked;
        page().querySelectorAll('.chkLibrary').forEach(input => {
            input.disabled = allLibraries;
            if (allLibraries) {
                input.checked = false;
            }
        });
    }

    function renderStatus(status) {
        const statusText = `${status.stage || 'Idle'} ${status.progressPercent || 0}%`;
        page().querySelector('#exportStatus').innerText = status.errorMessage ? `${statusText}: ${status.errorMessage}` : statusText;

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

    function refreshStatus() {
        return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('InventoryExporter/Status') }).then(renderStatus);
    }

    function refreshExports() {
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('InventoryExporter/Exports') }).then(exports => {
            page().querySelector('#exportHistory').innerHTML = exports.length === 0
                ? '<p>No exports yet.</p>'
                : exports.map(item => `<p><a href="${ApiClient.getUrl('InventoryExporter/Exports/' + item.id + '/Download')}">${item.fileName}</a> ${item.itemCount} items <button is="emby-button" type="button" data-delete-export="${item.id}">Delete</button></p>`).join('');
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
            }).then(() => {
                page().querySelector('#exportStatus').innerText = 'Starting export 0%';
                scheduleStatusRefresh();
            });
        }

        if (event.target.closest('#btnDownloadLatest')) {
            window.location.href = ApiClient.getUrl('InventoryExporter/Exports/Latest');
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
