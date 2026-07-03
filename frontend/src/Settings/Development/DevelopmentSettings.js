import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { inputTypes, kinds } from 'Helpers/Props';
import SettingsToolbarConnector from 'Settings/SettingsToolbarConnector';
import translate from 'Utilities/String/translate';

const logLevelOptions = [
  { key: 'info', value: 'Info' },
  { key: 'debug', value: 'Debug' },
  { key: 'trace', value: 'Trace' }
];

const metadataSourceOptions = [
  { key: '', value: 'Default (api.bookinfo.pro)' },
  { key: 'https://hardcover.bookinfo.pro/v1', value: 'Hardcover (hardcover.bookinfo.pro)' },
  { key: 'openlibrary', value: 'Open Library (direct)' },
  { key: 'custom', value: 'Custom URL' }
];

const metadataProtocolOptions = [
  { key: 'default', value: 'Default (GoodReads-compatible)' },
  { key: 'hardcover', value: 'Hardcover' }
];

function getSourceType(metadataSource) {
  if (!metadataSource) {
    return '';
  }

  if (metadataSource === 'openlibrary') {
    return 'openlibrary';
  }

  if (metadataSource === 'https://hardcover.bookinfo.pro/v1') {
    return 'https://hardcover.bookinfo.pro/v1';
  }

  // If it doesn't match a known preset, it's custom
  return 'custom';
}

class DevelopmentSettings extends Component {

  //
  // Lifecycle

  constructor(props) {
    super(props);

    const currentSource = props.settings?.metadataSource?.value || '';
    const sourceType = getSourceType(currentSource);

    this.state = {
      sourceType,
      customUrl: sourceType === 'custom' ? currentSource : ''
    };
  }

  //
  // Listeners

  onSourceTypeChange = ({ value }) => {
    this.setState({ sourceType: value });

    if (value === 'custom') {
      // Don't change metadataSource yet, wait for URL input
      return;
    }

    this.props.onInputChange({ name: 'metadataSource', value });
  };

  onCustomUrlChange = ({ value }) => {
    this.setState({ customUrl: value });
    this.props.onInputChange({ name: 'metadataSource', value });
  };

  //
  // Render

  render() {
    const {
      isFetching,
      error,
      settings,
      hasSettings,
      onInputChange,
      onSavePress,
      ...otherProps
    } = this.props;

    const { sourceType, customUrl } = this.state;

    const showBearerToken = sourceType === 'https://hardcover.bookinfo.pro/v1' || sourceType === 'custom';
    const showProtocol = sourceType === 'custom';
    const showCustomUrl = sourceType === 'custom';

    return (
      <PageContent title={translate('Development')}>
        <SettingsToolbarConnector
          {...otherProps}
          onSavePress={onSavePress}
        />

        <PageContentBody>
          {
            isFetching &&
              <LoadingIndicator />
          }

          {
            !isFetching && error &&
              <div>
                Unable to load Development settings
              </div>
          }

          {
            hasSettings && !isFetching && !error &&
              <Form
                id="developmentSettings"
                {...otherProps}
              >
                <FieldSet legend="Metadata Provider">
                  <Alert kind={kinds.INFO}>
                    Select the metadata source for book information. The default uses a community reading-glasses proxy backed by GoodReads data. Hardcover provides higher-quality curated metadata. Open Library is free and open-source. You can also specify a self-hosted reading-glasses instance.
                  </Alert>

                  <FormGroup>
                    <FormLabel>
                      Source
                    </FormLabel>

                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name="metadataSourceType"
                      values={metadataSourceOptions}
                      value={sourceType}
                      helpText="Choose the metadata provider for author and book information"
                      onChange={this.onSourceTypeChange}
                    />
                  </FormGroup>

                  {
                    showCustomUrl &&
                      <FormGroup>
                        <FormLabel>
                          Custom URL
                        </FormLabel>

                        <FormInputGroup
                          type={inputTypes.TEXT}
                          name="metadataSource"
                          value={customUrl}
                          helpText="Full URL to your reading-glasses instance (e.g., http://localhost:8788/v1)"
                          onChange={this.onCustomUrlChange}
                        />
                      </FormGroup>
                  }

                  {
                    showBearerToken &&
                      <FormGroup>
                        <FormLabel>
                          Bearer Token
                        </FormLabel>

                        <FormInputGroup
                          type={inputTypes.PASSWORD}
                          name="metadataSourceBearerToken"
                          helpText="Optional authentication token (full 'Bearer <token>' string). Required for Hardcover paid tiers."
                          onChange={onInputChange}
                          {...settings.metadataSourceBearerToken}
                        />
                      </FormGroup>
                  }

                  {
                    showProtocol &&
                      <FormGroup>
                        <FormLabel>
                          Protocol
                        </FormLabel>

                        <FormInputGroup
                          type={inputTypes.SELECT}
                          name="metadataSourceProtocol"
                          values={metadataProtocolOptions}
                          helpText="Select the API protocol your metadata server speaks"
                          onChange={onInputChange}
                          {...settings.metadataSourceProtocol}
                        />
                      </FormGroup>
                  }
                </FieldSet>

                <FieldSet legend={translate('Logging')}>
                  <FormGroup>
                    <FormLabel>
                      {translate('LogRotation')}
                    </FormLabel>

                    <FormInputGroup
                      type={inputTypes.NUMBER}
                      name="logRotate"
                      helpText={translate('LogRotateHelpText')}
                      onChange={onInputChange}
                      {...settings.logRotate}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>
                      {translate('ConsoleLogLevel')}
                    </FormLabel>
                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name="consoleLogLevel"
                      values={logLevelOptions}
                      onChange={onInputChange}
                      {...settings.consoleLogLevel}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>
                      {translate('LogSQL')}
                    </FormLabel>

                    <FormInputGroup
                      type={inputTypes.CHECK}
                      name="logSql"
                      helpText={translate('LogSqlHelpText')}
                      onChange={onInputChange}
                      {...settings.logSql}
                    />
                  </FormGroup>
                </FieldSet>

                <FieldSet legend={translate('Analytics')}>
                  <FormGroup>
                    <FormLabel>
                      {translate('FilterAnalyticsEvents')}
                    </FormLabel>

                    <FormInputGroup
                      type={inputTypes.CHECK}
                      name="filterSentryEvents"
                      helpText={translate('FilterSentryEventsHelpText')}
                      onChange={onInputChange}
                      {...settings.filterSentryEvents}
                    />
                  </FormGroup>
                </FieldSet>
              </Form>
          }
        </PageContentBody>
      </PageContent>
    );
  }

}

DevelopmentSettings.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  error: PropTypes.object,
  settings: PropTypes.object.isRequired,
  hasSettings: PropTypes.bool.isRequired,
  onSavePress: PropTypes.func.isRequired,
  onInputChange: PropTypes.func.isRequired
};

export default DevelopmentSettings;
