(function () {
    const pluginId = '7184fe02-8e91-4fd2-9140-6d58d5e91f0a';

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

    function refreshHistory() {
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('InventoryExporter/Libraries') }).then(libraries => {
            page().querySelector('#selLibraries').innerHTML = libraries.map(library => `<option value="${library.id}">${library.name}</option>`).join('');
        });
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('InventoryExporter/Status') }).then(status => {
            page().querySelector('#exportStatus').innerText = `${status.stage || 'Idle'} ${status.progressPercent || 0}%`;
        });
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('InventoryExporter/Exports') }).then(exports => {
            page().querySelector('#exportHistory').innerHTML = exports.map(item => `<p><a href="${ApiClient.getUrl('InventoryExporter/Exports/' + item.id + '/Download')}">${item.fileName}</a> ${item.itemCount} items <button is="emby-button" type="button" data-delete-export="${item.id}">Delete</button></p>`).join('');
        });
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
            const libraryIds = Array.from(page().querySelector('#selLibraries').selectedOptions).map(option => option.value);
            ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('InventoryExporter/Export'),
                data: JSON.stringify({ formats: ['csv', 'json'], libraryIds: libraryIds }),
                contentType: 'application/json'
            }).then(refreshHistory);
        }

        if (event.target.closest('#btnDownloadLatest')) {
            window.location.href = ApiClient.getUrl('InventoryExporter/Exports/Latest');
        }

        const deleteButton = event.target.closest('[data-delete-export]');
        if (deleteButton) {
            ApiClient.ajax({ type: 'DELETE', url: ApiClient.getUrl('InventoryExporter/Exports/' + deleteButton.getAttribute('data-delete-export')) }).then(refreshHistory);
        }
    });
})();
